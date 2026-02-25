using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Handles combat logic - cooldowns, damage calculation
/// This system doesn't trigger attacks, it just manages combat state
/// Actual attacks are triggered by player input or AI
/// </summary>
public class CombatSystem : AEntitySetSystem<float>
{
    public CombatSystem(World world)
        : base(world.GetEntities()
            .With<AttackComponent>()
            .With<CombatStateComponent>()
            .AsSet())
    {
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        ref var combat = ref entity.Get<CombatStateComponent>();
        ref var attack = ref entity.Get<AttackComponent>();

        // Combat state management
        if (combat.InCombat)
        {
            // Check if target is still valid
            if (combat.TargetEntityId > 0)
            {
                // Try to find target
                var entities = World.GetEntities()
                    .With<EntityIdComponent>()
                    .AsSet();

                bool targetFound = false;
                foreach (var targetEntity in entities.GetEntities())
                {
                    ref var targetId = ref targetEntity.Get<EntityIdComponent>();
                    if (targetId.EntityId == combat.TargetEntityId)
                    {
                        targetFound = true;

                        // Check if target is dead
                        if (targetEntity.Has<HealthComponent>())
                        {
                            ref var targetHealth = ref targetEntity.Get<HealthComponent>();
                            if (targetHealth.IsDead)
                            {
                                // Target died, exit combat
                                combat.InCombat = false;
                                combat.TargetEntityId = 0;
                            }
                        }
                        break;
                    }
                }

                if (!targetFound)
                {
                    // Target doesn't exist anymore, exit combat
                    combat.InCombat = false;
                    combat.TargetEntityId = 0;
                }
            }
        }
    }
}
