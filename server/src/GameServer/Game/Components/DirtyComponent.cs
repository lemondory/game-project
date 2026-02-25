namespace GameServer.Game.Components;

/// <summary>
/// Marks entity as changed and needing broadcast
/// </summary>
public struct DirtyComponent
{
    public bool PositionChanged;
    public bool HealthChanged;
    public bool CombatStateChanged;

    public bool IsAnyDirty => PositionChanged || HealthChanged || CombatStateChanged;

    public void Clear()
    {
        PositionChanged = false;
        HealthChanged = false;
        CombatStateChanged = false;
    }
}
