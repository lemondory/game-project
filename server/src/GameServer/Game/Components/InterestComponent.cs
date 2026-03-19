namespace GameServer.Game.Components;

/// <summary>
/// Tracks which entities this player has been told about (S2C_Spawn sent).
/// Class component — contains a mutable HashSet, so cannot be a struct.
/// Only assigned to player-controlled entities.
/// </summary>
public class InterestComponent
{
    public HashSet<long> VisibleEntityIds { get; } = new();
}
