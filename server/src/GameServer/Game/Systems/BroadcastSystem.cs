using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameShared.Enums;
using GameShared.Proto;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Broadcasts entity changes to clients in the same zone
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

        ref var zone = ref entity.Get<ZoneComponent>();
        ref var entityId = ref entity.Get<EntityIdComponent>();

        // Broadcast position changes
        if (dirty.PositionChanged && entity.Has<PositionComponent>())
        {
            ref var position = ref entity.Get<PositionComponent>();
            BroadcastPosition(zone.ZoneId, entityId.EntityId, position.Position);
        }

        // Broadcast health changes
        if (dirty.HealthChanged && entity.Has<HealthComponent>())
        {
            ref var health = ref entity.Get<HealthComponent>();
            BroadcastDamage(zone.ZoneId, entityId.EntityId, health.Current, health.Max);
        }

        // Clear dirty flags
        dirty.Clear();
    }

    private void BroadcastPosition(int zoneId, long entityId, GameShared.Utils.Vector3 position)
    {
        var packet = new S2C_Move
        {
            EntityId = entityId,
            Position = new Vec3 { X = position.X, Y = position.Y, Z = position.Z }
        };

        // Send to all players in the same zone
        var entities = _world.GetEntities()
            .With<SessionComponent>()
            .With<ZoneComponent>()
            .AsSet();

        foreach (var targetEntity in entities.GetEntities())
        {
            ref var targetZone = ref targetEntity.Get<ZoneComponent>();
            if (targetZone.ZoneId == zoneId)
            {
                ref var session = ref targetEntity.Get<SessionComponent>();
                if (session.Session.IsConnected)
                {
                    session.Session.Send(PacketId.S2C_Move, packet);
                }
            }
        }
    }

    private void BroadcastDamage(int zoneId, long entityId, int currentHp, int maxHp)
    {
        var packet = new S2C_Damage
        {
            TargetEntityId = entityId,
            Damage = maxHp - currentHp, // Simplified - just show total damage
            CurrentHp = currentHp
        };

        // Send to all players in the same zone
        var entities = _world.GetEntities()
            .With<SessionComponent>()
            .With<ZoneComponent>()
            .AsSet();

        foreach (var targetEntity in entities.GetEntities())
        {
            ref var targetZone = ref targetEntity.Get<ZoneComponent>();
            if (targetZone.ZoneId == zoneId)
            {
                ref var session = ref targetEntity.Get<SessionComponent>();
                if (session.Session.IsConnected)
                {
                    session.Session.Send(PacketId.S2C_Damage, packet);
                }
            }
        }
    }
}
