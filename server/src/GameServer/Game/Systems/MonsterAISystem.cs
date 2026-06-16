using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using GameServer.Game.Components;
using GameShared.Utils;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// FSM 기반 몬스터 AI.
/// 상태: Idle → Chase → Attack
///
/// Update 시작 시 entityById / alivePlayers를 갱신해 중첩 쿼리 없이 O(1) 조회.
/// 구조적 변경(DestinationComponent Add/Remove)은 쿼리 종료 후 일괄 처리.
/// </summary>
public class MonsterAISystem : BaseSystem<World, float>
{
    private const float AggroScanInterval = 0.5f;
    private const float IdleWanderTime    = 3f;
    private const float ChaseUpdateTime   = 0.5f;

    private readonly Random _random = new();

    private readonly QueryDescription _monsterQuery = new QueryDescription()
        .WithAll<AIComponent, MonsterComponent, PositionComponent, CombatStateComponent, EntityIdComponent>();
    private readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<PlayerComponent, PositionComponent, HealthComponent, EntityIdComponent>();
    private readonly QueryDescription _allQuery = new QueryDescription()
        .WithAll<EntityIdComponent>();

    // 프레임당 재사용 캐시
    private readonly Dictionary<long, Entity>                                               _entityById  = new();
    private readonly List<(Entity entity, Vector3 position, bool isDead, long entityId)>    _players     = new();

    // 쿼리 종료 후 적용할 구조적 변경 목록
    private readonly List<(Entity entity, DestinationComponent dest, VelocityComponent vel)> _toSetDest   = new();
    private readonly List<Entity>                                                             _toRemoveDest = new();

    public MonsterAISystem(World world) : base(world) { }

    public override void Update(in float state)
    {
        float deltaTime = state;

        // 프레임 시작: 조회 캐시 갱신
        _entityById.Clear();
        World.Query(in _allQuery, (Entity e, ref EntityIdComponent eid) =>
        {
            _entityById[eid.EntityId] = e;
        });

        _players.Clear();
        World.Query(in _playerQuery, (Entity e, ref PositionComponent pos, ref HealthComponent hp, ref EntityIdComponent eid) =>
        {
            _players.Add((e, pos.Position, hp.IsDead, eid.EntityId));
        });

        _toSetDest.Clear();
        _toRemoveDest.Clear();

        // 몬스터 AI 처리
        World.Query(in _monsterQuery, (Entity entity, ref AIComponent ai, ref PositionComponent position,
            ref CombatStateComponent combatState, ref EntityIdComponent entityId) =>
        {
            ai.StateTime += deltaTime;

            switch (ai.State)
            {
                case AIState.Idle:   UpdateIdle(entity, ref ai, ref position);                         break;
                case AIState.Chase:  UpdateChase(entity, ref ai, ref position);                        break;
                case AIState.Attack: UpdateAttack(entity, ref ai, ref position, ref combatState, entityId.EntityId); break;
            }
        });

        // 구조적 변경 일괄 적용
        foreach (var (e, dest, vel) in _toSetDest)
        {
            if (!e.IsAlive()) continue;
            var d = dest; var v = vel;
            if (e.Has<DestinationComponent>()) e.Set(d); else e.Add(d);
            if (e.Has<VelocityComponent>())    e.Set(v); else e.Add(v);
        }
        foreach (var e in _toRemoveDest)
        {
            if (!e.IsAlive()) continue;
            if (e.Has<DestinationComponent>()) e.Remove<DestinationComponent>();
            if (e.Has<VelocityComponent>())    e.Remove<VelocityComponent>();
        }
    }

    private void UpdateIdle(Entity entity, ref AIComponent ai, ref PositionComponent position)
    {
        if (ai.StateTime % AggroScanInterval < AggroScanInterval - 0.051f) return;

        var nearestPlayer = FindNearestAlivePlayer(position.Position, ai.AggroRange);
        if (nearestPlayer.HasValue)
        {
            long targetEntityId = nearestPlayer.Value.entityId;
            ai.TargetEntityId = targetEntityId;
            ai.State          = AIState.Chase;
            ai.StateTime      = 0f;
            Log.Debug("Monster → Chase {TargetId}", targetEntityId);
            return;
        }

        if (ai.StateTime >= IdleWanderTime)
        {
            float wanderDistance = 2f;
            var wanderTarget = new Vector3(
                position.Position.X + ((float)_random.NextDouble() * 2 - 1) * wanderDistance,
                position.Position.Y,
                position.Position.Z + ((float)_random.NextDouble() * 2 - 1) * wanderDistance
            );
            _toSetDest.Add((entity, new DestinationComponent(wanderTarget), new VelocityComponent(ai.ChaseSpeed * 0.4f, Vector3.Zero)));
            ai.StateTime = 0f;
        }
    }

    private void UpdateChase(Entity entity, ref AIComponent ai, ref PositionComponent position)
    {
        if (!_entityById.TryGetValue(ai.TargetEntityId, out var target) || IsEntityDead(target))
        {
            TransitionToIdle(entity, ref ai);
            return;
        }

        ref var targetPos = ref target.TryGetRef<PositionComponent>(out _);
        float distance = position.Position.Distance(targetPos.Position);

        if (distance <= ai.AttackRange)
        {
            ai.State     = AIState.Attack;
            ai.StateTime = 0f;
            _toRemoveDest.Add(entity);
            return;
        }

        if (distance > ai.AggroRange * 1.5f)
        {
            TransitionToIdle(entity, ref ai);
            return;
        }

        if (ai.StateTime >= ChaseUpdateTime)
        {
            _toSetDest.Add((entity, new DestinationComponent(targetPos.Position), new VelocityComponent(ai.ChaseSpeed, Vector3.Zero)));
            ai.StateTime = 0f;
        }
    }

    private void UpdateAttack(Entity entity, ref AIComponent ai, ref PositionComponent position,
        ref CombatStateComponent combatState, long selfEntityId)
    {
        if (!_entityById.TryGetValue(ai.TargetEntityId, out var target) || IsEntityDead(target))
        {
            combatState.InCombat       = false;
            combatState.TargetEntityId = 0;
            TransitionToIdle(entity, ref ai);
            return;
        }

        ref var targetPos = ref target.TryGetRef<PositionComponent>(out _);
        float distance = position.Position.Distance(targetPos.Position);

        if (distance > ai.AttackRange)
        {
            ai.State     = AIState.Chase;
            ai.StateTime = 0f;
            return;
        }

        combatState.InCombat       = true;
        combatState.TargetEntityId = ai.TargetEntityId;
    }

    private void TransitionToIdle(Entity entity, ref AIComponent ai)
    {
        ai.State          = AIState.Idle;
        ai.TargetEntityId = 0;
        ai.StateTime      = 0f;
        _toRemoveDest.Add(entity);
    }

    private (Entity entity, long entityId)? FindNearestAlivePlayer(Vector3 position, float range)
    {
        (Entity entity, long entityId)? nearest = null;
        float nearestDistSq = range * range;

        foreach (var (e, pos, isDead, eid) in _players)
        {
            if (isDead) continue;
            float dx = pos.X - position.X;
            float dz = pos.Z - position.Z;
            float distSq = dx * dx + dz * dz;
            if (distSq <= nearestDistSq)
            {
                nearest       = (e, eid);
                nearestDistSq = distSq;
            }
        }
        return nearest;
    }

    private static bool IsEntityDead(Entity entity)
        => entity.Has<HealthComponent>() && entity.Get<HealthComponent>().IsDead;
}
