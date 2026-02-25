using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameShared.Utils;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Simple FSM-based monster AI
/// States: Idle → Chase → Attack
/// </summary>
public class MonsterAISystem : AEntitySetSystem<float>
{
    private static readonly float IdleWanderTime = 3f; // Wander every 3 seconds
    private static readonly float ChaseUpdateTime = 0.5f; // Update chase path every 0.5s
    private readonly Random _random = new();

    public MonsterAISystem(World world)
        : base(world.GetEntities()
            .With<AIComponent>()
            .With<MonsterComponent>()
            .With<PositionComponent>()
            .AsSet())
    {
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        ref var ai = ref entity.Get<AIComponent>();
        ref var position = ref entity.Get<PositionComponent>();

        ai.StateTime += deltaTime;

        switch (ai.State)
        {
            case AIState.Idle:
                UpdateIdle(entity, ref ai, ref position, deltaTime);
                break;

            case AIState.Chase:
                UpdateChase(entity, ref ai, ref position, deltaTime);
                break;

            case AIState.Attack:
                UpdateAttack(entity, ref ai, ref position, deltaTime);
                break;
        }
    }

    private void UpdateIdle(in Entity entity, ref AIComponent ai, ref PositionComponent position, float deltaTime)
    {
        // Look for nearby players
        var nearestPlayer = FindNearestPlayer(position.Position, ai.AggroRange);

        if (nearestPlayer.HasValue)
        {
            // Found a target, switch to chase
            ref var targetId = ref nearestPlayer.Value.Get<EntityIdComponent>();
            ai.TargetEntityId = targetId.EntityId;
            ai.State = AIState.Chase;
            ai.StateTime = 0f;

            Log.Debug("Monster {EntityId} started chasing {TargetId}",
                entity.Get<EntityIdComponent>().EntityId, targetId.EntityId);
        }
        else
        {
            // No target, wander randomly
            if (ai.StateTime >= IdleWanderTime)
            {
                // Random wander (small movement)
                float wanderDistance = 2f;
                var wanderTarget = new Vector3(
                    position.Position.X + ((float)_random.NextDouble() * 2 - 1) * wanderDistance,
                    position.Position.Y,
                    position.Position.Z + ((float)_random.NextDouble() * 2 - 1) * wanderDistance
                );

                entity.Set(new DestinationComponent(wanderTarget));
                entity.Set(new VelocityComponent(1f, Vector3.Zero)); // Slow wander

                ai.StateTime = 0f;
            }
        }
    }

    private void UpdateChase(in Entity entity, ref AIComponent ai, ref PositionComponent position, float deltaTime)
    {
        // Find target
        var target = FindEntityById(ai.TargetEntityId);

        if (!target.HasValue)
        {
            // Target lost, return to idle
            ai.State = AIState.Idle;
            ai.TargetEntityId = 0;
            ai.StateTime = 0f;

            // Stop moving
            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();

            return;
        }

        ref var targetPos = ref target.Value.Get<PositionComponent>();
        float distance = position.Position.Distance(targetPos.Position);

        // Check if in attack range
        if (distance <= ai.AttackRange)
        {
            // Switch to attack
            ai.State = AIState.Attack;
            ai.StateTime = 0f;

            // Stop moving
            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();

            return;
        }

        // Check if target escaped aggro range
        if (distance > ai.AggroRange * 1.5f) // 1.5x aggro range for leash
        {
            // Target escaped, return to idle
            ai.State = AIState.Idle;
            ai.TargetEntityId = 0;
            ai.StateTime = 0f;

            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();

            Log.Debug("Monster {EntityId} lost target (leash)", entity.Get<EntityIdComponent>().EntityId);
            return;
        }

        // Update chase path periodically
        if (ai.StateTime >= ChaseUpdateTime)
        {
            entity.Set(new DestinationComponent(targetPos.Position));
            entity.Set(new VelocityComponent(3f, Vector3.Zero)); // Chase speed

            ai.StateTime = 0f;
        }
    }

    private void UpdateAttack(in Entity entity, ref AIComponent ai, ref PositionComponent position, float deltaTime)
    {
        // Find target
        var target = FindEntityById(ai.TargetEntityId);

        if (!target.HasValue)
        {
            // Target lost, return to idle
            ai.State = AIState.Idle;
            ai.TargetEntityId = 0;
            ai.StateTime = 0f;
            return;
        }

        ref var targetPos = ref target.Value.Get<PositionComponent>();
        float distance = position.Position.Distance(targetPos.Position);

        // Check if target moved out of attack range
        if (distance > ai.AttackRange)
        {
            // Return to chase
            ai.State = AIState.Chase;
            ai.StateTime = 0f;
            return;
        }

        // Try to attack
        if (entity.Has<AttackComponent>())
        {
            ref var attack = ref entity.Get<AttackComponent>();

            // Note: Actual attack execution should be handled by DungeonZone
            // This just sets the combat state
            if (!entity.Has<CombatStateComponent>())
            {
                entity.Set(new CombatStateComponent(true, ai.TargetEntityId));
            }
            else
            {
                ref var combat = ref entity.Get<CombatStateComponent>();
                combat.InCombat = true;
                combat.TargetEntityId = ai.TargetEntityId;
            }
        }
    }

    private Entity? FindNearestPlayer(Vector3 position, float range)
    {
        var players = World.GetEntities()
            .With<PlayerComponent>()
            .With<PositionComponent>()
            .AsSet();

        Entity? nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (var player in players.GetEntities())
        {
            ref var playerPos = ref player.Get<PositionComponent>();
            float distance = position.Distance(playerPos.Position);

            if (distance <= range && distance < nearestDistance)
            {
                nearest = player;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private Entity? FindEntityById(long entityId)
    {
        var entities = World.GetEntities()
            .With<EntityIdComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == entityId)
            {
                return entity;
            }
        }

        return null;
    }
}
