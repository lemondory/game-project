using System.Collections.Concurrent;
using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Systems;
using GameShared.Enums;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// Base class for all zones (Town, Dungeon, etc.)
/// </summary>
public abstract class Zone
{
    public int ZoneId { get; }
    public ZoneType ZoneType { get; }
    protected World World { get; }
    protected ISystem<float> Systems { get; }

    private readonly Thread _gameThread;
    private volatile bool _isRunning;
    private const float TickRate = 20f; // 20 Hz
    private const float FixedDeltaTime = 1f / TickRate; // 50ms

    // 네트워크 스레드에서 받은 엔티티 변경 작업을 게임루프 스레드에서 안전하게 처리
    private readonly ConcurrentQueue<Action> _pendingActions = new();

    /// <summary>게임루프 스레드에서 실행될 액션을 큐에 추가한다</summary>
    protected void EnqueueAction(Action action) => _pendingActions.Enqueue(action);

    protected Zone(int zoneId, ZoneType zoneType)
    {
        ZoneId = zoneId;
        ZoneType = zoneType;
        World = new World();
        Systems = CreateSystems();

        _gameThread = new Thread(GameLoop)
        {
            Name = $"Zone-{zoneId}-{zoneType}",
            IsBackground = true
        };
    }

    /// <summary>
    /// Create zone-specific systems
    /// </summary>
    protected virtual ISystem<float> CreateSystems()
    {
        return new SequentialSystem<float>(
            new MovementSystem(World),
            new BroadcastSystem(World),
            new SessionSystem(World)
        );
    }

    /// <summary>
    /// Start the zone's game loop
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _gameThread.Start();
        Log.Information("Zone started: {ZoneId} - {ZoneType}", ZoneId, ZoneType);
    }

    /// <summary>
    /// Stop the zone's game loop
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _gameThread.Join(TimeSpan.FromSeconds(5));
        Log.Information("Zone stopped: {ZoneId} - {ZoneType}", ZoneId, ZoneType);
    }

    /// <summary>
    /// Fixed tick rate game loop (20Hz)
    /// </summary>
    private void GameLoop()
    {
        var lastTime = DateTime.UtcNow;

        while (_isRunning)
        {
            var currentTime = DateTime.UtcNow;
            var deltaTime = (float)(currentTime - lastTime).TotalSeconds;
            lastTime = currentTime;

            try
            {
                // 네트워크 스레드에서 쌓인 엔티티 변경 작업을 먼저 처리
                while (_pendingActions.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { Log.Error(ex, "Error in pending action for zone {ZoneId}", ZoneId); }
                }

                // Update all systems
                Systems.Update(FixedDeltaTime);

                // Zone-specific update
                OnUpdate(FixedDeltaTime);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in zone game loop: {ZoneId}", ZoneId);
            }

            // Sleep to maintain tick rate
            var elapsedMs = (int)(DateTime.UtcNow - currentTime).TotalMilliseconds;
            var sleepTime = (int)(FixedDeltaTime * 1000) - elapsedMs;
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }
    }

    /// <summary>
    /// Zone-specific update logic
    /// </summary>
    protected virtual void OnUpdate(float deltaTime)
    {
        // Override in derived classes
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public virtual void Dispose()
    {
        Stop();
        Systems.Dispose();
        World.Dispose();
    }
}
