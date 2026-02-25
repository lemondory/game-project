// This is an EXAMPLE file showing how to extend GameDataManager
// After running DataConverter, create a similar file with your actual generated tables

/*
using GameShared.Generated.Data;

namespace GameShared.Data;

public partial class GameDataManager
{
    // Add properties for each generated data table
    public MonsterDataTable Monsters { get; private set; } = new();
    public ItemDataTable Items { get; private set; } = new();
    public SkillDataTable Skills { get; private set; } = new();
    public MapDataTable Maps { get; private set; } = new();

    protected override void LoadAllTables()
    {
        // Load each table from .bytes files
        Monsters = LoadTable<MonsterDataTable>("MonsterData.bytes");
        Items = LoadTable<ItemDataTable>("ItemData.bytes");
        Skills = LoadTable<SkillDataTable>("SkillData.bytes");
        Maps = LoadTable<MapDataTable>("MapData.bytes");
    }

    protected override void ClearAllTables()
    {
        Monsters = new MonsterDataTable();
        Items = new ItemDataTable();
        Skills = new SkillDataTable();
        Maps = new MapDataTable();
    }
}
*/

// Usage Example:

// Server (GameServer/Program.cs):
// GameDataManager.Instance.Initialize("data/bytes");
// var slime = GameDataManager.Instance.Monsters.GetById(1001);

// Unity Client:
// GameDataManager.Instance.Initialize(Application.streamingAssetsPath + "/Data");
// var sword = GameDataManager.Instance.Items.GetById(2001);
