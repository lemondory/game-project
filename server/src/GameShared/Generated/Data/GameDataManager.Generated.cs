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

    private ItemDataTable _itemData = new();
    private MonsterDataTable _monsterData = new();
    private SkillDataTable _skillData = new();
    private StringsTable _strings = new();

    /// <summary>Access to ItemData</summary>
    public static ItemDataTable ItemData => Instance._itemData;

    /// <summary>Access to MonsterData</summary>
    public static MonsterDataTable MonsterData => Instance._monsterData;

    /// <summary>Access to SkillData</summary>
    public static SkillDataTable SkillData => Instance._skillData;

    /// <summary>Access to Strings</summary>
    public static StringsTable Strings => Instance._strings;

    protected override void LoadAllTables()
    {
        _itemData = LoadTable<ItemDataTable>("ItemData.bytes");
        _monsterData = LoadTable<MonsterDataTable>("MonsterData.bytes");
        _skillData = LoadTable<SkillDataTable>("SkillData.bytes");
        _strings = LoadTable<StringsTable>("Strings.bytes");
    }

    protected override void ClearAllTables()
    {
        _itemData = new ItemDataTable();
        _monsterData = new MonsterDataTable();
        _skillData = new SkillDataTable();
        _strings = new StringsTable();
    }
}
