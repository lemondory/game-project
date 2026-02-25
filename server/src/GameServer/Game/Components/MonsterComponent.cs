using GameShared.Generated.Data;

namespace GameServer.Game.Components;

/// <summary>
/// Monster-specific data
/// </summary>
public struct MonsterComponent
{
    public int MonsterId;
    public MonsterData Data;

    public MonsterComponent(int monsterId, MonsterData data)
    {
        MonsterId = monsterId;
        Data = data;
    }
}
