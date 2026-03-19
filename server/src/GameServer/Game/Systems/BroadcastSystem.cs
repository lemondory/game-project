using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game;
using GameServer.Game.Components;
using GameServer.Game.Zones;
using GameShared.Enums;
using GameShared.Proto;

namespace GameServer.Game.Systems;

/// <summary>
/// Broadcasts entity position changes to clients in the same zone.
/// Damage/death are broadcast explicitly by the zone (DungeonZone), not here.
/// </summary>
public class BroadcastSystem : AEntitySetSystem<float>
{
    private readonly World _world;

    public BroadcastSystem(World world)
        : base(world.GetEntities()
            .With<DirtyComponent>()
            .With<ZoneComponent>()
            .AsSet())
    {
        _world = world;
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        ref var dirty = ref entity.Get<DirtyComponent>();
        if (!dirty.IsAnyDirty)
            return;

        ref var zone     = ref entity.Get<ZoneComponent>();
        ref var entityId = ref entity.Get<EntityIdComponent>();

        // Broadcast position changes
        if (dirty.PositionChanged && entity.Has<PositionComponent>())
        {
            ref var position = ref entity.Get<PositionComponent>();
            BroadcastPosition(zone.ZoneId, entityId.EntityId, position.Position);
        }

        dirty.Clear();
    }

    private void BroadcastPosition(int zoneId, long entityId, GameShared.Utils.Vector3 position)
    {
        var packet = new S2C_Move
        {
            EntityId = entityId,
            Position = new Vec3 { X = position.X, Y = position.Y, Z = position.Z }
        };

        var entities = _world.GetEntities()
            .With<SessionComponent>()
            .With<ZoneComponent>()
            .With<InterestComponent>()
            .AsSet();

        foreach (var targetEntity in entities.GetEntities())
        {
            ref var targetZone = ref targetEntity.Get<ZoneComponent>();
            if (targetZone.ZoneId != zoneId) continue;

            var interest = targetEntity.Get<InterestComponent>();
            if (!interest.VisibleEntityIds.Contains(entityId)) continue;

            ref var session = ref targetEntity.Get<SessionComponent>();
            if (session.Session.IsConnected)
            {
                session.Session.Send(PacketId.S2C_Move, packet);
                Interlocked.Increment(ref Zone.TotalMovePacketsSent);
            }
        }
    }
}
