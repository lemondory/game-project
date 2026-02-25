namespace GameServer.Game.Components;

/// <summary>
/// Unique entity identifier
/// </summary>
public struct EntityIdComponent
{
    public long EntityId;

    public EntityIdComponent(long entityId)
    {
        EntityId = entityId;
    }
}
