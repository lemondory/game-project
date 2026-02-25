namespace GameServer.Game.Components;

/// <summary>
/// Health points
/// </summary>
public struct HealthComponent
{
    public int Current;
    public int Max;

    public HealthComponent(int max)
    {
        Current = max;
        Max = max;
    }

    public HealthComponent(int current, int max)
    {
        Current = current;
        Max = max;
    }

    public bool IsDead => Current <= 0;
    public float Percentage => Max > 0 ? (float)Current / Max : 0f;
}
