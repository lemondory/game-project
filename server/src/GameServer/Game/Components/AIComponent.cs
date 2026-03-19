namespace GameServer.Game.Components;

/// <summary>
/// AI state component for monsters
/// </summary>
public struct AIComponent
{
    public AIState State;
    public long TargetEntityId;
    public float StateTime; // Time in current state
    public float AggroRange;  // Detection range
    public float AttackRange; // Attack range
    public float ChaseSpeed;  // 추격 이동 속도 (MonsterData.MoveSpeed)

    public AIComponent(float aggroRange, float attackRange, float chaseSpeed = 3f)
    {
        State = AIState.Idle;
        TargetEntityId = 0;
        StateTime = 0f;
        AggroRange = aggroRange;
        AttackRange = attackRange;
        ChaseSpeed = chaseSpeed;
    }
}

public enum AIState
{
    Idle,   // Standing still, looking for targets
    Chase,  // Moving towards target
    Attack  // Attacking target
}
