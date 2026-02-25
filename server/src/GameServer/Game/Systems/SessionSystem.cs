using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Manages disconnected sessions and removes entities
/// </summary>
public class SessionSystem : AEntitySetSystem<float>
{
    private readonly List<Entity> _entitiesToRemove = new();

    public SessionSystem(World world)
        : base(world.GetEntities()
            .With<SessionComponent>()
            .AsSet())
    {
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        // Skip if entity is already disposed
        if (!entity.IsAlive)
            return;

        ref var sessionComponent = ref entity.Get<SessionComponent>();

        // Check if session is disconnected
        if (!sessionComponent.Session.IsConnected)
        {
            // Log disconnection
            if (entity.Has<PlayerComponent>())
            {
                ref var player = ref entity.Get<PlayerComponent>();
                Log.Information("Player disconnected: {PlayerId} - {PlayerName}", player.PlayerId, player.Name);
            }

            if (entity.Has<EntityIdComponent>())
            {
                ref var entityId = ref entity.Get<EntityIdComponent>();
                Log.Debug("Removing entity: {EntityId}", entityId.EntityId);

                // Broadcast despawn if in a zone
                if (entity.Has<ZoneComponent>())
                {
                    BroadcastDespawn(entity);
                }
            }

            // Mark for removal (don't dispose during iteration)
            _entitiesToRemove.Add(entity);
        }
    }

    protected override void PostUpdate(float state)
    {
        // Remove entities after iteration is complete
        foreach (var entity in _entitiesToRemove)
        {
            if (entity.IsAlive)
            {
                entity.Dispose();
            }
        }
        _entitiesToRemove.Clear();
    }

    private void BroadcastDespawn(in Entity entity)
    {
        ref var entityId = ref entity.Get<EntityIdComponent>();
        ref var zone = ref entity.Get<ZoneComponent>();

        var packet = new GameShared.Proto.S2C_Despawn
        {
            EntityId = entityId.EntityId
        };

        // Send to all players in the same zone
        var entities = World.GetEntities()
            .With<SessionComponent>()
            .With<ZoneComponent>()
            .AsSet();

        foreach (var targetEntity in entities.GetEntities())
        {
            if (targetEntity == entity)
                continue; // Skip self

            ref var targetZone = ref targetEntity.Get<ZoneComponent>();
            if (targetZone.ZoneId == zone.ZoneId)
            {
                ref var session = ref targetEntity.Get<SessionComponent>();
                if (session.Session.IsConnected)
                {
                    session.Session.Send(GameShared.Enums.PacketId.S2C_Despawn, packet);
                }
            }
        }
    }
}
