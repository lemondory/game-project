using System.Collections.Concurrent;
using GameShared.Enums;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// Manages all zones (Town, Dungeon instances)
/// </summary>
public class ZoneManager
{
    private static ZoneManager? _instance;
    public static ZoneManager Instance => _instance ??= new ZoneManager();

    private readonly ConcurrentDictionary<int, Zone> _zones = new();
    private TownZone? _townZone;
    private int _nextDungeonZoneId = 1000;

    private ZoneManager()
    {
    }

    /// <summary>
    /// Initialize the zone manager
    /// </summary>
    public void Initialize()
    {
        // Create and start the singleton town zone
        _townZone = new TownZone();
        _zones[_townZone.ZoneId] = _townZone;
        _townZone.Start();

        Log.Information("ZoneManager initialized with TownZone");
    }

    /// <summary>
    /// Get the town zone
    /// </summary>
    public TownZone GetTownZone()
    {
        if (_townZone == null)
            throw new InvalidOperationException("ZoneManager not initialized");

        return _townZone;
    }

    /// <summary>
    /// Get a zone by ID
    /// </summary>
    public Zone? GetZone(int zoneId)
    {
        _zones.TryGetValue(zoneId, out var zone);
        return zone;
    }

    /// <summary>
    /// Create a new dungeon instance
    /// </summary>
    public DungeonZone CreateDungeonZone(int dungeonId)
    {
        var zoneId = Interlocked.Increment(ref _nextDungeonZoneId);

        var dungeonZone = new DungeonZone(zoneId, dungeonId);
        _zones[zoneId] = dungeonZone;
        dungeonZone.Start();

        Log.Information("Dungeon instance created: ZoneId={ZoneId}, DungeonId={DungeonId}", zoneId, dungeonId);

        // Spawn initial monsters
        SpawnMonstersForDungeon(dungeonZone, dungeonId);

        return dungeonZone;
    }

    private void SpawnMonstersForDungeon(DungeonZone dungeon, int dungeonId)
    {
        // Spawn 3-5 monsters around the dungeon
        var random = new Random();
        int monsterCount = random.Next(3, 6);

        for (int i = 0; i < monsterCount; i++)
        {
            var position = new GameShared.Utils.Vector3(
                random.Next(-10, 11),
                0,
                random.Next(-10, 11)
            );

            dungeon.SpawnMonster(1, position); // MonsterId = 1 for now
        }

        Log.Information("Spawned {Count} monsters in dungeon {ZoneId}", monsterCount, dungeon.ZoneId);
    }

    /// <summary>
    /// Remove a zone (for dungeon instances)
    /// </summary>
    public void RemoveZone(int zoneId)
    {
        if (_zones.TryRemove(zoneId, out var zone))
        {
            zone.Dispose();
            Log.Information("Zone removed: {ZoneId}", zoneId);
        }
    }

    /// <summary>
    /// Shutdown all zones
    /// </summary>
    public void Shutdown()
    {
        Log.Information("Shutting down all zones...");

        foreach (var zone in _zones.Values)
        {
            zone.Dispose();
        }

        _zones.Clear();
        _townZone = null;

        Log.Information("All zones shut down");
    }
}
