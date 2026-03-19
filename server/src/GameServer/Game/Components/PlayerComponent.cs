namespace GameServer.Game.Components;

/// <summary>
/// Player-specific data
/// </summary>
public struct PlayerComponent
{
    public long PlayerId;
    public string Name;
    public int Level;
    public int Exp;
    public int Gold;
    public int ClassId; // References CharacterClassData

    public PlayerComponent(long playerId, string name, int level = 1, int exp = 0, int gold = 0, int classId = 1)
    {
        PlayerId = playerId;
        Name = name;
        Level = level;
        Exp = exp;
        Gold = gold;
        ClassId = classId;
    }
}
