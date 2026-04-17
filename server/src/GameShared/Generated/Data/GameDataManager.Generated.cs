#nullable enable

// Auto-generated file by DataConverter
// DO NOT EDIT MANUALLY

using GameShared.Generated.Data;

namespace GameShared.Data;

/// <summary>
/// Auto-generated GameDataManager class
/// All data tables are automatically registered
/// </summary>
public sealed class GameDataManager : DataManagerBase
{
    private static GameDataManager? _instance;
    private static readonly object _lock = new object();

    /// <summary>Singleton instance</summary>
    public static GameDataManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new GameDataManager();
                }
            }
            return _instance;
        }
    }

    private GameDataManager() : base()
    {
        // Private constructor for singleton
    }

    private CharacterClassDataTable _characterClassData = new();
    private DungeonDataTable _dungeonData = new();
    private ItemDataTable _itemData = new();
    private MapObjectDataTable _mapObjectData = new();
    private MonsterDataTable _monsterData = new();
    private SkillDataTable _skillData = new();
    private StringsTable _strings = new();
    private TimeLimitedFieldDataTable _timeLimitedFieldData = new();

    /// <summary>Access to CharacterClassData</summary>
    public static CharacterClassDataTable CharacterClassData => Instance._characterClassData;

    /// <summary>Access to DungeonData</summary>
    public static DungeonDataTable DungeonData => Instance._dungeonData;

    /// <summary>Access to ItemData</summary>
    public static ItemDataTable ItemData => Instance._itemData;

    /// <summary>Access to MapObjectData</summary>
    public static MapObjectDataTable MapObjectData => Instance._mapObjectData;

    /// <summary>Access to MonsterData</summary>
    public static MonsterDataTable MonsterData => Instance._monsterData;

    /// <summary>Access to SkillData</summary>
    public static SkillDataTable SkillData => Instance._skillData;

    /// <summary>Access to Strings</summary>
    public static StringsTable Strings => Instance._strings;

    /// <summary>Access to TimeLimitedFieldData</summary>
    public static TimeLimitedFieldDataTable TimeLimitedFieldData => Instance._timeLimitedFieldData;

    protected override void LoadAllTables()
    {
        _characterClassData = LoadTable<CharacterClassDataTable>("CharacterClassData.bytes");
        _dungeonData = LoadTable<DungeonDataTable>("DungeonData.bytes");
        _itemData = LoadTable<ItemDataTable>("ItemData.bytes");
        _mapObjectData = LoadTable<MapObjectDataTable>("MapObjectData.bytes");
        _monsterData = LoadTable<MonsterDataTable>("MonsterData.bytes");
        _skillData = LoadTable<SkillDataTable>("SkillData.bytes");
        _strings = LoadTable<StringsTable>("Strings.bytes");
        _timeLimitedFieldData = LoadTable<TimeLimitedFieldDataTable>("TimeLimitedFieldData.bytes");
    }

    protected override void ClearAllTables()
    {
        _characterClassData = new CharacterClassDataTable();
        _dungeonData = new DungeonDataTable();
        _itemData = new ItemDataTable();
        _mapObjectData = new MapObjectDataTable();
        _monsterData = new MonsterDataTable();
        _skillData = new SkillDataTable();
        _strings = new StringsTable();
        _timeLimitedFieldData = new TimeLimitedFieldDataTable();
    }
}
