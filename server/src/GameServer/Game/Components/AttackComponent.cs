namespace GameServer.Game.Components;

/// <summary>
/// Attack capability
/// </summary>
public struct AttackComponent
{
    public int Power;
    public float Range;
    public float Cooldown; // seconds
    public float LastAttackTime; // timestamp

    public AttackComponent(int power, float range, float cooldown)
    {
        Power = power;
        Range = range;
        Cooldown = cooldown;
        LastAttackTime = 0f;
    }

    public bool CanAttack(float currentTime)
    {
        return currentTime - LastAttackTime >= Cooldown;
    }
}
