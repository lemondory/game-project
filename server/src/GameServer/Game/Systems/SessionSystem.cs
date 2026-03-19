using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Detects disconnected sessions and removes their entities from the world.
/// S2C_Despawn is NOT sent here — AoiSystem handles it on the next tick
/// when the removed entity disappears from players' visible sets.
/// </summary>
public class SessionSystem : AEntitySetSystem<float>
{
    private readonly List<Entity> _entitiesToRemove = new();
    private readonly AoiGrid?     _aoiGrid;

    public SessionSystem(World world, AoiGrid? aoiGrid = null)
        : base(world.GetEntities()
            .With<SessionComponent>()
            .AsSet())
    {
        _aoiGrid = aoiGrid;
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        if (!entity.IsAlive)
            return;

        ref var session = ref entity.Get<SessionComponent>();
        if (session.Session.IsConnected)
            return;

        if (entity.Has<PlayerComponent>())
        {
            ref var player = ref entity.Get<PlayerComponent>();
            Log.Information("Player disconnected: {PlayerId} - {PlayerName}", player.PlayerId, player.Name);
        }

        if (entity.Has<EntityIdComponent>())
            Log.Debug("Removing entity: {EntityId}", entity.Get<EntityIdComponent>().EntityId);

        _entitiesToRemove.Add(entity);
    }

    protected override void PostUpdate(float state)
    {
        foreach (var entity in _entitiesToRemove)
        {
            if (!entity.IsAlive) continue;

            // Remove from AOI grid before disposing so AoiSystem can detect the absence
            // and send S2C_Despawn to interested players on the next tick.
            if (_aoiGrid != null && entity.Has<EntityIdComponent>())
                _aoiGrid.Remove(entity.Get<EntityIdComponent>().EntityId);

            entity.Dispose();
        }
        _entitiesToRemove.Clear();
    }
}
