using System.Collections.Concurrent;
using System.Diagnostics;
using DefaultEcs;
using DefaultEcs.System;
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
    protected World World { get; }
    protected ISystem<float> Systems { get; }
    protected AoiGrid AoiGrid { get; } = new AoiGrid();

    private readonly Thread _gameThread;
    private volatile bool _isRunning;
    private const float TickRate      = 20f;           // 20 Hz
    private const float FixedDeltaTime = 1f / TickRate; // 50ms

    // 네트워크 스레드에서 받은 엔티티 변경 작업을 게임루프 스레드에서 안전하게 처리
    private readonly ConcurrentQueue<Action> _pendingActions = new();

    /// <summary>게임루프 스레드에서 실행될 액션을 큐에 추가한다</summary>
    protected void EnqueueAction(Action action) => _pendingActions.Enqueue(action);

    // ── 성능 메트릭 ──────────────────────────────────────────────────────────
    // 내부 카운터 — 게임루프 단일 스레드 전용이므로 lock 불필요
    public static long TotalMovePacketsSent;      // BroadcastSystem/AoiSystem에서 직접 증가

    private readonly EntitySet _entityCountSet;   // With<EntityIdComponent> — 항상 최신 유지
    private const    int       MetricIntervalTicks = 100; // 100틱 = 5초 (20 Hz)
    private          int       _metricTickCount;
    private          long      _metricTotalMs;
    private          long      _metricMaxMs;

    protected Zone(int zoneId, ZoneType zoneType)
    {
        ZoneId   = zoneId;
        ZoneType = zoneType;
        World    = new World();

        // EntitySet은 World가 생성된 직후 바로 만들어야 한다 (CreateSystems 이전)
        _entityCountSet = World.GetEntities().With<EntityIdComponent>().AsSet();

        Systems = CreateSystems();

        _gameThread = new Thread(GameLoop)
        {
            Name         = $"Zone-{zoneId}-{zoneType}",
            IsBackground = true
        };
    }

    /// <summary>
    /// 존별 ECS 시스템 파이프라인을 구성한다.
    /// 서브클래스에서 오버라이드하여 존 고유 시스템을 추가할 수 있다.
    /// (예: DungeonZone → MonsterAISystem, CombatSystem 추가)
    /// </summary>
    protected virtual ISystem<float> CreateSystems()
    {
        return new SequentialSystem<float>(
            new MovementSystem(World),
            new AoiSystem(World, AoiGrid),
            new BroadcastSystem(World),
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
                // 네트워크 스레드에서 큐잉된 액션들을 게임루프에서 순차 실행
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

            // ── 메트릭 누적 ────────────────────────────────────────────────
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

            // ── 틱 레이트 유지 ─────────────────────────────────────────────
            var totalElapsedMs = (int)(DateTime.UtcNow - currentTime).TotalMilliseconds;
            var sleepTime      = (int)(FixedDeltaTime * 1000) - totalElapsedMs;
            if (sleepTime > 0)
                Thread.Sleep(sleepTime);
        }
    }

    private void LogMetrics()
    {
        int    entities   = _entityCountSet.Count;
        double avgMs      = _metricTotalMs / (double)_metricTickCount;
        long   budget     = (long)(FixedDeltaTime * 1000); // 50ms
        long   movePkts   = Interlocked.Read(ref TotalMovePacketsSent);

        Log.Information(
            "[Zone {ZoneId}/{ZoneType}] entities={Entities} | " +
            "tick avg={Avg:F1}ms max={Max}ms (budget {Budget}ms) | " +
            "S2C_Move total={MovePkts}",
            ZoneId, ZoneType, entities,
            avgMs, _metricMaxMs, budget,
            movePkts);
    }

    // ── 공용 핸들러 (Town/Dungeon 공통) ─────────────────────────────────────

    /// <summary>엔티티의 이동 목적지를 설정한다. 게임루프 스레드에서 안전하게 실행된다.</summary>
    public void HandleMove(long entityId, Vector3 destination, float speed)
    {
        EnqueueAction(() =>
        {
            foreach (var entity in _entityCountSet.GetEntities())
            {
                ref var id = ref entity.Get<EntityIdComponent>();
                if (id.EntityId != entityId) continue;

                entity.Set(new DestinationComponent(destination));
                entity.Set(new VelocityComponent(speed, new Vector3()));
                Log.Debug("엔티티 이동: EntityId={EntityId}, 목적지=({X},{Y},{Z})",
                    entityId, destination.X, destination.Y, destination.Z);
                break;
            }
        });
    }

    /// <summary>채팅 메시지를 존 내 모든 플레이어에게 브로드캐스트한다.</summary>
    public void HandleChat(long entityId, string message)
    {
        EnqueueAction(() =>
        {
            // 발신자 이름 찾기
            string senderName = string.Empty;
            foreach (var entity in _entityCountSet.GetEntities())
            {
                ref var id = ref entity.Get<EntityIdComponent>();
                if (id.EntityId != entityId) continue;
                if (entity.Has<PlayerComponent>())
                    senderName = entity.Get<PlayerComponent>().Name;
                break;
            }

            if (string.IsNullOrEmpty(senderName)) return;

            var packet = new S2C_Chat { SenderName = senderName, Message = message };
            foreach (var entity in _entityCountSet.GetEntities())
            {
                if (!entity.Has<SessionComponent>()) continue;
                ref var session = ref entity.Get<SessionComponent>();
                if (session.Session.IsConnected)
                    session.Session.Send(PacketId.S2C_Chat, packet);
            }

            Log.Information("[Zone {ZoneId}] 채팅: [{PlayerName}] {Message}", ZoneId, senderName, message);
        });
    }

    protected virtual void OnUpdate(float deltaTime) { }

    public virtual void Dispose()
    {
        Stop();
        _entityCountSet.Dispose();
        Systems.Dispose();
        World.Dispose();
    }
}
