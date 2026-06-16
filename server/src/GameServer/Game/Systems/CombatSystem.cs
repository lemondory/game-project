using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using GameServer.Game.Components;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Monster auto-attack: processes entities in combat and applies damage each cooldown tick.
/// Damage, death, and rewards are reported via callbacks so the zone can broadcast them.
/// </summary>
public class CombatSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _attackerQuery = new QueryDescription()
        .WithAll<AttackComponent, CombatStateComponent, EntityIdComponent>();
    private readonly QueryDescription _targetQuery   = new QueryDescription()
        .WithAll<EntityIdComponent, HealthComponent>();

    private readonly Action<long, long>?          _onAttack;
    private readonly Action<long, int, int, int>? _onDamage;
    private readonly Action<Entity, Entity>?       _onDeath;

    // 프레임당 재사용 — 매 프레임 할당 없음
    private readonly Dictionary<long, Entity>                                               _entityById    = new();
    private readonly List<(long attackerId, long targetId, int damage, Entity a, Entity t)> _pendingEvents = new();

    public CombatSystem(World world,
        Action<long, long>?          onAttack = null,
        Action<long, int, int, int>? onDamage = null,
        Action<Entity, Entity>?      onDeath  = null)
        : base(world)
    {
        _onAttack = onAttack;
        _onDamage = onDamage;
        _onDeath  = onDeath;
    }

    public override void Update(in float state)
    {
        // Step 1: 타겟 조회 딕셔너리 구성 (쿼리 내 중첩 쿼리 방지)
        _entityById.Clear();
        World.Query(in _targetQuery, (Entity e, ref EntityIdComponent eid) =>
        {
            _entityById[eid.EntityId] = e;
        });

        float currentTime = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;
        _pendingEvents.Clear();

        // Step 2: 전투 중인 엔티티 처리 — 데미지는 여기서 바로 적용 (값 변경이므로 안전)
        World.Query(in _attackerQuery, (Entity attacker, ref AttackComponent attack, ref CombatStateComponent combat, ref EntityIdComponent attackerId) =>
        {
            if (!combat.InCombat || combat.TargetEntityId <= 0) return;
            if (!attack.CanAttack(currentTime)) return;
            if (!_entityById.TryGetValue(combat.TargetEntityId, out var target)) { combat.InCombat = false; combat.TargetEntityId = 0; return; }

            ref var targetHealth = ref target.TryGetRef<HealthComponent>(out bool exists);
            if (!exists || targetHealth.IsDead) { combat.InCombat = false; combat.TargetEntityId = 0; return; }

            int targetDefense = target.Has<DefenseComponent>() ? target.Get<DefenseComponent>().Defense : 0;
            int damage = Math.Max(1, attack.Power - targetDefense);

            targetHealth.Current  = Math.Max(0, targetHealth.Current - damage);
            attack.LastAttackTime = currentTime;

            Log.Debug("Auto-attack: {Attacker} → {Target}, Dmg={Damage}, HP={Current}/{Max}",
                attackerId.EntityId, combat.TargetEntityId, damage, targetHealth.Current, targetHealth.Max);

            _pendingEvents.Add((attackerId.EntityId, combat.TargetEntityId, damage, attacker, target));

            if (targetHealth.IsDead)
            {
                combat.InCombat       = false;
                combat.TargetEntityId = 0;
            }
        });

        // Step 3: 콜백은 쿼리 종료 후 호출 (구조적 변경 가능한 콜백 안전 처리)
        foreach (var (attackerId, targetId, damage, attacker, target) in _pendingEvents)
        {
            _onAttack?.Invoke(attackerId, targetId);

            if (!target.IsAlive()) continue;
            var health = target.Get<HealthComponent>();
            _onDamage?.Invoke(targetId, damage, health.Current, health.Max);

            if (health.IsDead)
                _onDeath?.Invoke(target, attacker);
        }
    }
}
