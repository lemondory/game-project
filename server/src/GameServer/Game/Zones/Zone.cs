using System.Collections.Concurrent;
using System.Diagnostics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using GameServer.Game;
using GameServer.Game.Components;
using GameServer.Game.Systems;
using GameServer.Network;
using GameShared.Enums;
using GameShared.Proto;
using GameShared.Utils;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// 모든 존(Town, Dungeon 등)의 베이스 클래스.
/// 각 존은 독립적인 게임루프 스레드(20Hz)에서 ECS 시스템을 실행하며,
/// 네트워크 스레드의 요청은 ConcurrentQueue를 통해 안전하게 처리한다.
/// </summary>
public abstract class Zone
{
    public int ZoneId { get; }
    public ZoneType ZoneType { get; }
    protected World          World         { get; }
    protected ISystem<float> Systems       { get; }
    protected AoiGrid        AoiGrid       { get; } = new AoiGrid();
    protected SubscriberMap  SubscriberMap { get; } = new SubscriberMap();

    private readonly Thread _gameThread;
    private volatile bool _isRunning;
    private const float TickRate       = 20f;
    private const float FixedDeltaTime = 1f / TickRate; // 50ms

    // 네트워크 스레드에서 받은 엔티티 변경 작업을 게임루프 스레드에서 안전하게 처리
    private readonly ConcurrentQueue<Action> _pendingActions = new();

    /// <summary>게임루프 스레드에서 실행될 액션을 큐에 추가한다</summary>
    protected void EnqueueAction(Action action) => _pendingActions.Enqueue(action);

    /// <summary>
    /// 입장 시 초기 NearbyEntities를 pre-populate할 때 SubscriberMap 구독을 함께 설정한다.
    /// </summary>
    public void SubscribeInitialEntities(ISession session, IEnumerable<long> entityIds)
    {
        foreach (var entityId in entityIds)
            SubscriberMap.Subscribe(entityId, session);
    }

    // ── 성능 메트릭 ──────────────────────────────────────────────────────────
    public static long TotalMovePacketsSent;

    private readonly QueryDescription _entityCountQuery = new QueryDescription().WithAll<EntityIdComponent>();
    private const    int              MetricIntervalTicks = 100; // 100틱 = 5초
    private          int              _metricTickCount;
    private          long             _metricTotalMs;
    private          long             _metricMaxMs;

    // Handle 이동 시 엔티티 탐색용 공통 쿼리
    private readonly QueryDescription _allEntitiesQuery = new QueryDescription().WithAll<EntityIdComponent>();

    protected Zone(int zoneId, ZoneType zoneType)
    {
        ZoneId   = zoneId;
        ZoneType = zoneType;
        World    = World.Create();
        Systems  = CreateSystems();

        _gameThread = new Thread(GameLoop)
        {
            Name         = $"Zone-{zoneId}-{zoneType}",
            IsBackground = true
        };
    }

    protected virtual ISystem<float> CreateSystems()
    {
        return new Group<float>($"Zone-{ZoneId}",
            new MovementSystem(World),
            new AoiSystem(World, AoiGrid, SubscriberMap),
            new BroadcastSystem(World, SubscriberMap),
            new SessionSystem(World, AoiGrid)
        );
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _gameThread.Start();
        Log.Information("존 시작: {ZoneId} - {ZoneType}", ZoneId, ZoneType);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _gameThread.Join(TimeSpan.FromSeconds(5));
        Log.Information("존 종료: {ZoneId} - {ZoneType}", ZoneId, ZoneType);
    }

    private void GameLoop()
    {
        var lastTime = DateTime.UtcNow;
        var sw       = new Stopwatch();

        while (_isRunning)
        {
            var currentTime = DateTime.UtcNow;
            var deltaTime   = (float)(currentTime - lastTime).TotalSeconds;
            lastTime = currentTime;

            sw.Restart();
            try
            {
                while (_pendingActions.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { Log.Error(ex, "존 {ZoneId}: 큐 액션 실행 중 오류", ZoneId); }
                }

                Systems.Update(FixedDeltaTime);
                OnUpdate(FixedDeltaTime);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "존 {ZoneId}: 게임루프 오류", ZoneId);
            }
            sw.Stop();

            long elapsedMs = sw.ElapsedMilliseconds;
            _metricTotalMs += elapsedMs;
            if (elapsedMs > _metricMaxMs) _metricMaxMs = elapsedMs;
            _metricTickCount++;

            if (_metricTickCount >= MetricIntervalTicks)
            {
                LogMetrics();
                _metricTickCount = 0;
                _metricTotalMs   = 0;
                _metricMaxMs     = 0;
            }

            var totalElapsedMs = (int)(DateTime.UtcNow - currentTime).TotalMilliseconds;
            var sleepTime      = (int)(FixedDeltaTime * 1000) - totalElapsedMs;
            if (sleepTime > 0) Thread.Sleep(sleepTime);
        }
    }

    private void LogMetrics()
    {
        int    entities = World.CountEntities(in _entityCountQuery);
        double avgMs    = _metricTotalMs / (double)_metricTickCount;
        long   budget   = (long)(FixedDeltaTime * 1000);
        long   movePkts = Interlocked.Read(ref TotalMovePacketsSent);

        Log.Information(
            "[Zone {ZoneId}/{ZoneType}] entities={Entities} | " +
            "tick avg={Avg:F1}ms max={Max}ms (budget {Budget}ms) | " +
            "S2C_Move total={MovePkts}",
            ZoneId, ZoneType, entities,
            avgMs, _metricMaxMs, budget, movePkts);
    }

    // ── 공용 핸들러 ─────────────────────────────────────────────────────────────

    public void HandleMove(long entityId, Vector3 destination, float speed)
    {
        EnqueueAction(() =>
        {
            World.Query(in _allEntitiesQuery, (Entity entity, ref EntityIdComponent id) =>
            {
                if (id.EntityId != entityId) return;

                var dest = new DestinationComponent(destination);
                var vel  = new VelocityComponent(speed, new Vector3());
                if (entity.Has<DestinationComponent>()) entity.Set(dest); else entity.Add(dest);
                if (entity.Has<VelocityComponent>())    entity.Set(vel);  else entity.Add(vel);

                Log.Debug("엔티티 이동: EntityId={EntityId}, 목적지=({X},{Y},{Z})",
                    entityId, destination.X, destination.Y, destination.Z);
            });
        });
    }

    public void HandleChat(long entityId, string message)
    {
        EnqueueAction(() =>
        {
            string senderName = string.Empty;
            World.Query(in _allEntitiesQuery, (Entity entity, ref EntityIdComponent id) =>
            {
                if (id.EntityId != entityId) return;
                if (entity.Has<PlayerComponent>())
                    senderName = entity.Get<PlayerComponent>().Name;
            });

            if (string.IsNullOrEmpty(senderName)) return;

            var packet      = new S2C_Chat { SenderName = senderName, Message = message };
            var sessionQuery = new QueryDescription().WithAll<EntityIdComponent, SessionComponent>();
            World.Query(in sessionQuery, (ref SessionComponent session) =>
            {
                if (session.Session.IsConnected)
                    session.Session.Send(PacketId.S2C_Chat, packet);
            });

            Log.Information("[Zone {ZoneId}] 채팅: [{PlayerName}] {Message}", ZoneId, senderName, message);
        });
    }

    protected virtual void OnUpdate(float deltaTime) { }

    public virtual void Dispose()
    {
        Stop();
        Systems.Dispose();
        World.Destroy(World);
    }
}
