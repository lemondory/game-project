namespace GameServer.Game.Components;

/// <summary>
/// Current combat state
/// </summary>
public struct CombatStateComponent
{
    public bool InCombat;
    public long TargetEntityId;

    public CombatStateComponent(bool inCombat, long targetEntityId = 0)
    {
        InCombat = inCombat;
        TargetEntityId = targetEntityId;
    }
}
