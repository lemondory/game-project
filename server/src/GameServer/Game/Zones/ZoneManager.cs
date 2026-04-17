using System.Collections.Concurrent;
using System.Linq;
using GameShared.Data;
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

    // 필드 존: fieldId → TimeLimitedFieldZone
    private readonly Dictionary<int, TimeLimitedFieldZone> _fieldZones = new();
    private int _nextFieldZoneId = 2000;

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

        // 모든 시간제 필드 존 생성 및 시작
        InitializeFieldZones();

        Log.Information("ZoneManager initialized");
    }

    private void InitializeFieldZones()
    {
        var allFields = GameDataManager.TimeLimitedFieldData.GetAll();
        foreach (var fieldData in allFields)
        {
            var zoneId = Interlocked.Increment(ref _nextFieldZoneId);
            var fieldZone = new TimeLimitedFieldZone(zoneId, fieldData.FieldId);
            _zones[zoneId] = fieldZone;
            _fieldZones[fieldData.FieldId] = fieldZone;

            // 초기 몬스터 스폰 (게임루프 시작 전)
            SpawnMonstersForField(fieldZone, fieldData);

            fieldZone.Start();
            Log.Information("TimeLimitedFieldZone started: ZoneId={ZoneId}, FieldId={FieldId}, Name={Name}",
                zoneId, fieldData.FieldId, fieldData.Name);
        }
    }

    private void SpawnMonstersForField(TimeLimitedFieldZone zone, GameShared.Generated.Data.TimeLimitedFieldData fieldData)
    {
        var monsterIds = fieldData.MonsterIds.Where(id => id > 0).ToArray();
        if (monsterIds.Length == 0) return;

        var random = new Random();
        for (int i = 0; i < monsterIds.Length; i++)
        {
            int col = i % 5;
            int row = i / 5;
            var position = new GameShared.Utils.Vector3(
                (col - 2) * 5f + (float)(random.NextDouble() * 2 - 1),
                0,
                row * 5f + 5f + (float)(random.NextDouble() * 2 - 1)
            );
            zone.SpawnMonster(monsterIds[i], position);
        }

        Log.Information("Spawned {Count} monsters in field ZoneId={ZoneId} (FieldId={FieldId})",
            monsterIds.Length, zone.ZoneId, zone.FieldId);
    }

    /// <summary>FieldId로 시간제 필드 존을 반환한다.</summary>
    public TimeLimitedFieldZone? GetFieldZone(int fieldId)
    {
        _fieldZones.TryGetValue(fieldId, out var zone);
        return zone;
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

        // 게임루프 시작 전에 초기 몬스터를 스폰해야 한다.
        // Start() 이후에 스폰하면 게임루프 스레드가 이미 실행 중인 상태에서
        // 메인 스레드가 World를 수정하는 race condition이 발생한다.
        SpawnMonstersForDungeon(dungeonZone, dungeonId);

        dungeonZone.Start();

        Log.Information("Dungeon instance created: ZoneId={ZoneId}, DungeonId={DungeonId}", zoneId, dungeonId);

        return dungeonZone;
    }

    private void SpawnMonstersForDungeon(DungeonZone dungeon, int dungeonId)
    {
        var dungeonData = GameDataManager.DungeonData.GetById(dungeonId);
        if (dungeonData == null)
        {
            Log.Warning("SpawnMonstersForDungeon: DungeonId={DungeonId} not found in data, skipping spawn", dungeonId);
            return;
        }

        var random = new Random();
        var monsterIds = dungeonData.MonsterIds.Where(id => id > 0).ToArray();

        if (monsterIds.Length == 0)
        {
            Log.Warning("SpawnMonstersForDungeon: DungeonId={DungeonId} has no monsters defined", dungeonId);
            return;
        }

        for (int i = 0; i < monsterIds.Length; i++)
        {
            // 몬스터를 격자 형태로 배치 (최대 5열)
            int col = i % 5;
            int row = i / 5;
            var position = new GameShared.Utils.Vector3(
                (col - 2) * 4f + (float)(random.NextDouble() * 2 - 1),
                0,
                row * 5f + 5f + (float)(random.NextDouble() * 2 - 1)
            );
            dungeon.SpawnMonster(monsterIds[i], position);
        }

        Log.Information("Spawned {Count} monsters in dungeon {ZoneId} (DungeonId={DungeonId}: {Name})",
            monsterIds.Length, dungeon.ZoneId, dungeonId, dungeonData.Name);
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
