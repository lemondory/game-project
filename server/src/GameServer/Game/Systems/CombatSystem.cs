using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Monster auto-attack: processes entities in combat and applies damage each cooldown tick.
/// Damage, death, and rewards are reported via callbacks so the zone can broadcast them.
/// </summary>
public class CombatSystem : AEntitySetSystem<float>
{
    private readonly EntitySet _targetableEntities;
    private readonly Action<long, long>? _onAttack;           // attackerEntityId, targetEntityId
    private readonly Action<long, int, int, int>? _onDamage;  // targetEntityId, damage, currentHp, maxHp
    private readonly Action<Entity, Entity>? _onDeath;        // deadEntity, killerEntity

    public CombatSystem(World world,
        Action<long, long>? onAttack = null,
        Action<long, int, int, int>? onDamage = null,
        Action<Entity, Entity>? onDeath = null)
        : base(world.GetEntities()
            .With<AttackComponent>()
            .With<CombatStateComponent>()
            .AsSet())
    {
        _targetableEntities = world.GetEntities()
            .With<EntityIdComponent>()
            .With<HealthComponent>()
            .AsSet();
        _onAttack = onAttack;
        _onDamage = onDamage;
        _onDeath = onDeath;
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        ref var combat = ref entity.Get<CombatStateComponent>();
        ref var attack = ref entity.Get<AttackComponent>();

        if (!combat.InCombat || combat.TargetEntityId <= 0)
            return;

        float currentTime = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;
        if (!attack.CanAttack(currentTime))
            return;

        // Find target by entity ID
        Entity? targetEntity = null;
        foreach (var t in _targetableEntities.GetEntities())
        {
            if (t.Get<EntityIdComponent>().EntityId == combat.TargetEntityId)
            {
                targetEntity = t;
                break;
            }
        }

        if (!targetEntity.HasValue)
        {
            combat.InCombat = false;
            combat.TargetEntityId = 0;
            return;
        }

        ref var targetHealth = ref targetEntity.Value.Get<HealthComponent>();
        if (targetHealth.IsDead)
        {
            combat.InCombat = false;
            combat.TargetEntityId = 0;
            return;
        }

        // Apply damage — 방어력 공식: Damage = max(1, AttackPower - Defense)
        int targetDefense = targetEntity.Value.Has<DefenseComponent>()
            ? targetEntity.Value.Get<DefenseComponent>().Defense : 0;
        int damage = Math.Max(1, attack.Power - targetDefense);

        targetHealth.Current = Math.Max(0, targetHealth.Current - damage);
        attack.LastAttackTime = currentTime;

        ref var attackerEntityId = ref entity.Get<EntityIdComponent>();
        Log.Debug("Auto-attack: {Attacker} → {Target}, Dmg={Damage}, HP={Current}/{Max}",
            attackerEntityId.EntityId, combat.TargetEntityId, damage, targetHealth.Current, targetHealth.Max);

        _onAttack?.Invoke(attackerEntityId.EntityId, combat.TargetEntityId);
        _onDamage?.Invoke(combat.TargetEntityId, damage, targetHealth.Current, targetHealth.Max);

        if (targetHealth.IsDead)
        {
            combat.InCombat = false;
            combat.TargetEntityId = 0;
            _onDeath?.Invoke(targetEntity.Value, entity);
        }
    }

    public override void Dispose()
    {
        _targetableEntities.Dispose();
        base.Dispose();
    }
}
