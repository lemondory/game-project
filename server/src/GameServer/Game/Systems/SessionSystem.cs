using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using GameServer.Game.Components;
using Serilog;

namespace GameServer.Game.Systems;

/// <summary>
/// Detects disconnected sessions and removes their entities from the world.
/// S2C_Despawn is NOT sent here — AoiSystem handles it on the next tick
/// when the removed entity disappears from players' visible sets.
/// </summary>
public class SessionSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<SessionComponent>();

    private readonly List<Entity> _entitiesToRemove = new();
    private readonly AoiGrid?    _aoiGrid;

    public SessionSystem(World world, AoiGrid? aoiGrid = null) : base(world)
    {
        _aoiGrid = aoiGrid;
    }

    public override void Update(in float state)
    {
        _entitiesToRemove.Clear();

        World.Query(in _query, (Entity entity, ref SessionComponent session) =>
        {
            if (session.Session.IsConnected) return;

            if (entity.Has<PlayerComponent>())
            {
                var player = entity.Get<PlayerComponent>();
                Log.Information("Player disconnected: {PlayerId} - {PlayerName}", player.PlayerId, player.Name);
            }

            if (entity.Has<EntityIdComponent>())
                Log.Debug("Removing entity: {EntityId}", entity.Get<EntityIdComponent>().EntityId);

            _entitiesToRemove.Add(entity);
        });

        foreach (var entity in _entitiesToRemove)
        {
            if (!entity.IsAlive()) continue;

            if (_aoiGrid != null && entity.Has<EntityIdComponent>())
                _aoiGrid.Remove(entity.Get<EntityIdComponent>().EntityId);

            World.Destroy(entity);
        }
    }
}
