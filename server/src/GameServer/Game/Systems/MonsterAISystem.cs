using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameShared.Utils;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// FSM 기반 몬스터 AI.
/// 상태: Idle → Chase → Attack
///
/// EntitySet을 생성자에서 한 번만 생성해 재사용한다.
/// FindNearestAlivePlayer / FindEntityById는 매 호출마다 AsSet()을 새로 만들지 않는다.
/// Idle 탐지 주기(AggroScanInterval)를 두어 매 틱 전체 플레이어 순회를 줄인다.
/// </summary>
public class MonsterAISystem : AEntitySetSystem<float>
{
    // Idle 상태에서 어그로 탐지를 수행하는 주기 (초)
    // 매 틱(50ms)이 아닌 0.5초마다 스캔 — 플레이어 수가 많을수록 효과적
    private const float AggroScanInterval = 0.5f;
    private const float IdleWanderTime    = 3f;
    private const float ChaseUpdateTime   = 0.5f;

    private readonly Random     _random = new();

    // 생성자에서 한 번만 생성 — 매 호출 AsSet() 할당 없음
    private readonly EntitySet _playerSet;
    private readonly EntitySet _allEntitySet;

    public MonsterAISystem(World world)
        : base(world.GetEntities()
            .With<AIComponent>()
            .With<MonsterComponent>()
            .With<PositionComponent>()
            .AsSet())
    {
        _playerSet = world.GetEntities()
            .With<PlayerComponent>()
            .With<PositionComponent>()
            .With<HealthComponent>()
            .AsSet();

        _allEntitySet = world.GetEntities()
            .With<EntityIdComponent>()
            .AsSet();
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        ref var ai       = ref entity.Get<AIComponent>();
        ref var position = ref entity.Get<PositionComponent>();

        ai.StateTime += deltaTime;

        switch (ai.State)
        {
            case AIState.Idle:   UpdateIdle(entity, ref ai, ref position);   break;
            case AIState.Chase:  UpdateChase(entity, ref ai, ref position);  break;
            case AIState.Attack: UpdateAttack(entity, ref ai, ref position); break;
        }
    }

    private void UpdateIdle(in Entity entity, ref AIComponent ai, ref PositionComponent position)
    {
        // AggroScanInterval마다만 플레이어 탐지 수행
        // StateTime은 상태 진입 후 경과 시간 — 진입 직후(0~interval)는 스캔 생략
        if (ai.StateTime % AggroScanInterval > AggroScanInterval - 0.051f)
            return; // 아직 스캔 주기가 아님 (50ms = 1틱 오차 허용)

        var nearestPlayer = FindNearestAlivePlayer(position.Position, ai.AggroRange);
        if (nearestPlayer.HasValue)
        {
            ref var targetId = ref nearestPlayer.Value.Get<EntityIdComponent>();
            ai.TargetEntityId = targetId.EntityId;
            ai.State          = AIState.Chase;
            ai.StateTime      = 0f;

            Log.Debug("Monster {EntityId} → Chase {TargetId}",
                entity.Get<EntityIdComponent>().EntityId, targetId.EntityId);
            return;
        }

        // 타겟 없음 — IdleWanderTime마다 배회
        if (ai.StateTime >= IdleWanderTime)
        {
            float wanderDistance = 2f;
            var wanderTarget = new Vector3(
                position.Position.X + ((float)_random.NextDouble() * 2 - 1) * wanderDistance,
                position.Position.Y,
                position.Position.Z + ((float)_random.NextDouble() * 2 - 1) * wanderDistance
            );

            entity.Set(new DestinationComponent(wanderTarget));
            entity.Set(new VelocityComponent(ai.ChaseSpeed * 0.4f, Vector3.Zero));
            ai.StateTime = 0f;
        }
    }

    private void UpdateChase(in Entity entity, ref AIComponent ai, ref PositionComponent position)
    {
        var target = FindEntityById(ai.TargetEntityId);

        // 타겟이 없거나 죽었으면 Idle 복귀
        if (!target.HasValue || IsEntityDead(target.Value))
        {
            TransitionToIdle(entity, ref ai);
            return;
        }

        ref var targetPos = ref target.Value.Get<PositionComponent>();
        float   distance  = position.Position.Distance(targetPos.Position);

        // 공격 범위 내 진입 → Attack
        if (distance <= ai.AttackRange)
        {
            ai.State     = AIState.Attack;
            ai.StateTime = 0f;
            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();
            return;
        }

        // 어그로 범위 이탈 → Idle (leash = 1.5x)
        if (distance > ai.AggroRange * 1.5f)
        {
            TransitionToIdle(entity, ref ai);
            return;
        }

        // ChaseUpdateTime마다 경로 갱신
        if (ai.StateTime >= ChaseUpdateTime)
        {
            entity.Set(new DestinationComponent(targetPos.Position));
            entity.Set(new VelocityComponent(ai.ChaseSpeed, Vector3.Zero));
            ai.StateTime = 0f;
        }
    }

    private void UpdateAttack(in Entity entity, ref AIComponent ai, ref PositionComponent position)
    {
        var target = FindEntityById(ai.TargetEntityId);

        // 타겟이 없거나 죽었으면 Idle 복귀
        if (!target.HasValue || IsEntityDead(target.Value))
        {
            ref var combat2 = ref entity.Get<CombatStateComponent>();
            combat2.InCombat      = false;
            combat2.TargetEntityId = 0;
            TransitionToIdle(entity, ref ai);
            return;
        }

        ref var targetPos = ref target.Value.Get<PositionComponent>();
        float   distance  = position.Position.Distance(targetPos.Position);

        // 공격 범위 이탈 → Chase 복귀
        if (distance > ai.AttackRange)
        {
            ai.State     = AIState.Chase;
            ai.StateTime = 0f;
            return;
        }

        // 전투 상태 설정 (CombatSystem이 실제 공격 처리)
        if (!entity.Has<CombatStateComponent>())
        {
            entity.Set(new CombatStateComponent(true, ai.TargetEntityId));
        }
        else
        {
            ref var combat = ref entity.Get<CombatStateComponent>();
            combat.InCombat       = true;
            combat.TargetEntityId = ai.TargetEntityId;
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    private void TransitionToIdle(in Entity entity, ref AIComponent ai)
    {
        ai.State          = AIState.Idle;
        ai.TargetEntityId = 0;
        ai.StateTime      = 0f;
        entity.Remove<DestinationComponent>();
        entity.Remove<VelocityComponent>();
    }

    /// <summary>범위 내 살아있는 가장 가까운 플레이어를 반환한다.</summary>
    private Entity? FindNearestAlivePlayer(Vector3 position, float range)
    {
        Entity? nearest         = null;
        float   nearestDistSq   = range * range;

        foreach (var player in _playerSet.GetEntities())
        {
            ref var health = ref player.Get<HealthComponent>();
            if (health.IsDead) continue;

            ref var playerPos = ref player.Get<PositionComponent>();
            float dx = playerPos.Position.X - position.X;
            float dz = playerPos.Position.Z - position.Z;
            float distSq = dx * dx + dz * dz;

            if (distSq <= nearestDistSq)
            {
                nearest       = player;
                nearestDistSq = distSq;
            }
        }

        return nearest;
    }

    /// <summary>EntityId로 엔티티를 찾는다.</summary>
    private Entity? FindEntityById(long entityId)
    {
        foreach (var entity in _allEntitySet.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == entityId)
                return entity;
        }
        return null;
    }

    private static bool IsEntityDead(Entity entity)
        => entity.Has<HealthComponent>() && entity.Get<HealthComponent>().IsDead;

    public override void Dispose()
    {
        base.Dispose();
        _playerSet.Dispose();
        _allEntitySet.Dispose();
    }
}
