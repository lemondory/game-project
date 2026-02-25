using DefaultEcs;
using GameServer.Game.Components;
using GameServer.Game.Systems;
using GameShared.Utils;

namespace GameServer.Tests;

public class MovementSystemTests : IDisposable
{
    private readonly World _world = new();
    private readonly MovementSystem _system;

    public MovementSystemTests()
    {
        _system = new MovementSystem(_world);
    }

    public void Dispose()
    {
        _system.Dispose();
        _world.Dispose();
    }

    private Entity CreateMovingEntity(Vector3 start, Vector3 target, float speed)
    {
        var entity = _world.CreateEntity();
        entity.Set(new PositionComponent(start));
        entity.Set(new VelocityComponent { Speed = speed });
        entity.Set(new DestinationComponent(target));
        return entity;
    }

    [Fact]
    public void Update_MovesEntityTowardsDestination()
    {
        var entity = CreateMovingEntity(
            new Vector3 { X = 0, Y = 0, Z = 0 },
            new Vector3 { X = 10, Y = 0, Z = 0 },
            speed: 5f);

        _system.Update(1f); // 1초, speed=5 → X=5로 이동

        var pos = entity.Get<PositionComponent>().Position;
        Assert.True(pos.X > 0, "엔티티가 목적지 방향으로 이동해야 함");
        Assert.True(pos.X < 10, "목적지를 초과하지 않아야 함");
        Assert.Equal(5f, pos.X, precision: 4);
    }

    [Fact]
    public void Update_RemovesComponentsOnArrival()
    {
        // 도착 임계값(0.1f) 이내에 위치시켜 즉시 도착 처리
        var entity = CreateMovingEntity(
            new Vector3 { X = 9.95f, Y = 0, Z = 0 },
            new Vector3 { X = 10,   Y = 0, Z = 0 },
            speed: 5f);

        _system.Update(1f);

        Assert.False(entity.Has<DestinationComponent>(), "도착 시 DestinationComponent가 제거되어야 함");
        Assert.False(entity.Has<VelocityComponent>(),    "도착 시 VelocityComponent가 제거되어야 함");
    }

    [Fact]
    public void Update_SetsDirtyFlagOnMovement()
    {
        var entity = CreateMovingEntity(
            new Vector3 { X = 0, Y = 0, Z = 0 },
            new Vector3 { X = 10, Y = 0, Z = 0 },
            speed: 5f);

        _system.Update(1f);

        Assert.True(entity.Has<DirtyComponent>(), "이동 후 DirtyComponent가 설정되어야 함");
        Assert.True(entity.Get<DirtyComponent>().PositionChanged, "PositionChanged가 true이어야 함");
    }

    [Fact]
    public void Update_SnapsToDestinationWhenOvershoot()
    {
        var target = new Vector3 { X = 10, Y = 0, Z = 0 };
        // speed * deltaTime(100 * 1 = 100) >> distance(2) → 목적지에 스냅
        var entity = CreateMovingEntity(
            new Vector3 { X = 8f, Y = 0, Z = 0 },
            target,
            speed: 100f);

        _system.Update(1f);

        var pos = entity.Get<PositionComponent>().Position;
        Assert.Equal(target.X, pos.X);
        Assert.Equal(target.Y, pos.Y);
        Assert.Equal(target.Z, pos.Z);
    }
}
