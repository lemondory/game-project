using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using GameServer.Game.Components;
using GameShared.Enums;
using GameShared.Proto;

namespace GameServer.Game.Systems;

/// <summary>
/// 플레이어별 관심 영역(AOI)을 관리한다.
/// MovementSystem 다음에 실행되어 그리드 좌표가 최신 상태임을 보장한다.
///
/// 엔티티가 시야에 진입하면 S2C_Spawn, 벗어나면 S2C_Despawn을 전송하고
/// SubscriberMap을 동기화하여 BroadcastSystem이 전체 플레이어 순회 없이
/// 구독자에게 직접 S2C_Move를 전달할 수 있게 한다.
/// </summary>
public class AoiSystem : BaseSystem<World, float>
{
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

    private readonly AoiGrid       _grid;
    private readonly SubscriberMap _subscriberMap;

    private readonly QueryDescription _dirtyQuery = new QueryDescription()
        .WithAll<DirtyComponent, PositionComponent, EntityIdComponent>();
    private readonly QueryDescription _allQuery = new QueryDescription()
        .WithAll<EntityIdComponent, PositionComponent, HealthComponent>();
    private readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<SessionComponent, InterestComponent, PositionComponent, EntityIdComponent>();

    // 매 틱 재사용 — 할당 없음
    private readonly Dictionary<long, Entity> _entityLookup   = new();
    private readonly HashSet<long>            _currentVisible = new();
    private readonly List<long>               _leftView       = new();

    public AoiSystem(World world, AoiGrid grid, SubscriberMap subscriberMap) : base(world)
    {
        _grid          = grid;
        _subscriberMap = subscriberMap;
    }

    public override void Update(in float state)
    {
        // Step 1: 이동한 엔티티 그리드 동기화
        World.Query(in _dirtyQuery, (ref DirtyComponent dirty, ref EntityIdComponent eid, ref PositionComponent pos) =>
        {
            if (!dirty.PositionChanged) return;
            _grid.Update(eid.EntityId, pos.Position.X, pos.Position.Z);
        });

        // Step 2: 엔티티 조회 딕셔너리 갱신
        _entityLookup.Clear();
        World.Query(in _allQuery, (Entity entity, ref EntityIdComponent eid) =>
        {
            _entityLookup[eid.EntityId] = entity;
        });

        // Step 3: 플레이어별 관심 집합 재계산
        World.Query(in _playerQuery, (Entity playerEntity, ref SessionComponent session,
            ref InterestComponent interest, ref PositionComponent selfPos, ref EntityIdComponent selfId) =>
        {
            if (!session.Session.IsConnected) return;

            float px = selfPos.Position.X;
            float pz = selfPos.Position.Z;

            _currentVisible.Clear();
            foreach (var candidateId in _grid.GetEntityIdsInRange(px, pz, ViewRadius))
            {
                if (candidateId == selfId.EntityId) continue;
                if (!_entityLookup.TryGetValue(candidateId, out var candidate)) continue;
                if (!candidate.IsAlive()) continue;

                ref var cPos = ref candidate.TryGetRef<PositionComponent>(out _);
                float dx = cPos.Position.X - px;
                float dz = cPos.Position.Z - pz;
                if (dx * dx + dz * dz <= ViewRadiusSq)
                    _currentVisible.Add(candidateId);
            }

            // 새로 시야에 진입 → S2C_Spawn + SubscriberMap 등록
            foreach (var newId in _currentVisible)
            {
                if (interest.VisibleEntityIds.Contains(newId)) continue;
                if (!_entityLookup.TryGetValue(newId, out var newEntity) || !newEntity.IsAlive()) continue;

                session.Session.Send(PacketId.S2C_Spawn, BuildSpawnPacket(newEntity));
                interest.VisibleEntityIds.Add(newId);
                _subscriberMap.Subscribe(newId, session.Session);
            }

            // 시야에서 벗어남 → S2C_Despawn + SubscriberMap 해제
            _leftView.Clear();
            foreach (var oldId in interest.VisibleEntityIds)
            {
                if (!_currentVisible.Contains(oldId))
                    _leftView.Add(oldId);
            }
            foreach (var leftId in _leftView)
            {
                session.Session.Send(PacketId.S2C_Despawn, new S2C_Despawn { EntityId = leftId });
                interest.VisibleEntityIds.Remove(leftId);
                _subscriberMap.Unsubscribe(leftId, session.Session);
            }
        });
    }

    private static S2C_Spawn BuildSpawnPacket(Entity entity)
    {
        ref var eid    = ref entity.TryGetRef<EntityIdComponent>(out _);
        ref var pos    = ref entity.TryGetRef<PositionComponent>(out _);
        ref var health = ref entity.TryGetRef<HealthComponent>(out _);

        string name;
        GameShared.Proto.EntityType type;

        if (entity.Has<PlayerComponent>())
        {
            name = entity.Get<PlayerComponent>().Name;
            type = GameShared.Proto.EntityType.Player;
        }
        else if (entity.Has<MonsterComponent>())
        {
            name = entity.Get<MonsterComponent>().Data.Name;
            type = GameShared.Proto.EntityType.Monster;
        }
        else
        {
            name = string.Empty;
            type = GameShared.Proto.EntityType.Player;
        }

        return new S2C_Spawn
        {
            Entity = new EntityInfo
            {
                EntityId   = eid.EntityId,
                EntityType = type,
                Name       = name,
                Position   = new Vec3 { X = pos.Position.X, Y = pos.Position.Y, Z = pos.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            }
        };
    }
}
