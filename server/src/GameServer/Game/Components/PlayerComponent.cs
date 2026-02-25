namespace GameServer.Game.Components;

/// <summary>
/// Player-specific data
/// </summary>
public struct PlayerComponent
{
    public long PlayerId;
    public string Name;

    public PlayerComponent(long playerId, string name)
    {
        PlayerId = playerId;
        Name = name;
    }
}
