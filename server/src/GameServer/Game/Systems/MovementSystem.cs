using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameShared.Utils;

namespace GameServer.Game.Systems;

/// <summary>
/// Handles entity movement towards destination
/// </summary>
public class MovementSystem : AEntitySetSystem<float>
{
    public MovementSystem(World world)
        : base(world.GetEntities()
            .With<PositionComponent>()
            .With<VelocityComponent>()
            .With<DestinationComponent>()
            .AsSet())
    {
    }

    protected override void Update(float deltaTime, in Entity entity)
    {
        // Guard: DefaultEcs swaps removed entities into processed slots during iteration.
        // An entity that had its components removed earlier in the same frame may reappear
        // at the end of the span — skip it to prevent IndexOutOfRangeException.
        if (!entity.Has<VelocityComponent>() || !entity.Has<DestinationComponent>())
            return;

        ref var position = ref entity.Get<PositionComponent>();
        ref var velocity = ref entity.Get<VelocityComponent>();
        ref var destination = ref entity.Get<DestinationComponent>();

        var current = position.Position;
        var target = destination.Target;

        // Calculate direction
        var direction = new Vector3
        {
            X = target.X - current.X,
            Y = target.Y - current.Y,
            Z = target.Z - current.Z
        };

        var distance = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);

        // Check if arrived
        if (distance <= destination.ArrivalThreshold)
        {
            // Arrived at destination
            position.Position = target;
            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();

            // Mark as dirty for broadcast
            if (entity.Has<DirtyComponent>())
            {
                ref var dirty = ref entity.Get<DirtyComponent>();
                dirty.PositionChanged = true;
            }
            else
            {
                entity.Set(new DirtyComponent { PositionChanged = true });
            }
            return;
        }

        // Normalize direction
        direction.X /= distance;
        direction.Y /= distance;
        direction.Z /= distance;

        // Move towards target
        var moveDistance = velocity.Speed * deltaTime;
        if (moveDistance >= distance)
        {
            // Will arrive this frame
            position.Position = target;
            entity.Remove<DestinationComponent>();
            entity.Remove<VelocityComponent>();
        }
        else
        {
            // Continue moving
            position.Position = new Vector3
            {
                X = current.X + direction.X * moveDistance,
                Y = current.Y + direction.Y * moveDistance,
                Z = current.Z + direction.Z * moveDistance
            };
        }

        // Mark as dirty for broadcast
        if (entity.Has<DirtyComponent>())
        {
            ref var dirty = ref entity.Get<DirtyComponent>();
            dirty.PositionChanged = true;
        }
        else
        {
            entity.Set(new DirtyComponent { PositionChanged = true });
        }
    }
}
