using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game;
using GameServer.Game.Components;
using GameServer.Game.Zones;
using GameShared.Enums;
using GameShared.Proto;

namespace GameServer.Game.Systems;

/// <summary>
/// 이동한 엔티티의 위치를 구독 중인 플레이어에게만 S2C_Move로 전송한다.
///
/// SubscriberMap을 통해 "해당 엔티티를 보고 있는 세션" 목록을 O(1)로 조회하므로
/// 전체 플레이어를 순회하는 O(N) 스캔이 발생하지 않는다.
/// 데미지/사망 브로드캐스트는 DungeonZone에서 명시적으로 처리한다.
/// </summary>
public class BroadcastSystem : AEntitySetSystem<float>
{
    private readonly SubscriberMap _subscriberMap;

    public BroadcastSystem(World world, SubscriberMap subscriberMap)
        : base(world.GetEntities()
            .With<DirtyComponent>()
            .With<ZoneComponent>()
            .AsSet())
    {
        _subscriberMap = subscriberMap;
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        ref var dirty = ref entity.Get<DirtyComponent>();
        if (!dirty.PositionChanged || !entity.Has<PositionComponent>())
            return;

        ref var entityId = ref entity.Get<EntityIdComponent>();
        ref var position = ref entity.Get<PositionComponent>();

        var packet = new S2C_Move
        {
            EntityId = entityId.EntityId,
            Position = new Vec3 { X = position.Position.X, Y = position.Position.Y, Z = position.Position.Z }
        };

        // SubscriberMap에서 이 엔티티를 구독 중인 세션만 직접 조회 — 전체 플레이어 순회 없음
        foreach (var session in _subscriberMap.GetSubscribers(entityId.EntityId))
        {
            if (session.IsConnected)
            {
                session.Send(PacketId.S2C_Move, packet);
                Interlocked.Increment(ref Zone.TotalMovePacketsSent);
            }
        }

        dirty.Clear();
    }
}
