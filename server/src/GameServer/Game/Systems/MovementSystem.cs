using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using GameServer.Game.Components;
using GameShared.Utils;

namespace GameServer.Game.Systems;

public class MovementSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<PositionComponent, VelocityComponent, DestinationComponent>();

    private readonly List<(Entity entity, bool arrived)> _processed = new();

    public MovementSystem(World world) : base(world) { }

    public override void Update(in float state)
    {
        float deltaTime = state;
        _processed.Clear();

        World.Query(in _query, (Entity entity, ref PositionComponent position, ref VelocityComponent velocity, ref DestinationComponent destination) =>
        {
            var current = position.Position;
            var target  = destination.Target;

            var direction = new Vector3
            {
                X = target.X - current.X,
                Y = target.Y - current.Y,
                Z = target.Z - current.Z
            };

            var distance = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
            bool arrived = false;

            if (distance <= destination.ArrivalThreshold)
            {
                position.Position = target;
                arrived = true;
            }
            else
            {
                direction.X /= distance;
                direction.Y /= distance;
                direction.Z /= distance;

                var moveDistance = velocity.Speed * deltaTime;
                if (moveDistance >= distance)
                {
                    position.Position = target;
                    arrived = true;
                }
                else
                {
                    position.Position = new Vector3
                    {
                        X = current.X + direction.X * moveDistance,
                        Y = current.Y + direction.Y * moveDistance,
                        Z = current.Z + direction.Z * moveDistance
                    };
                }
            }

            _processed.Add((entity, arrived));
        });

        // Structural changes (Add/Remove) must be deferred until after the query
        foreach (var (entity, arrived) in _processed)
        {
            if (!entity.IsAlive()) continue;

            if (arrived)
            {
                entity.Remove<DestinationComponent>();
                entity.Remove<VelocityComponent>();
            }

            ref var dirty = ref entity.AddOrGet<DirtyComponent>(default);
            dirty.PositionChanged = true;
        }
    }
}
