using GameShared.Utils;

namespace GameServer.Game.Components;

/// <summary>
/// Entity movement velocity
/// </summary>
public struct VelocityComponent
{
    public float Speed;
    public Vector3 Direction;

    public VelocityComponent(float speed, Vector3 direction)
    {
        Speed = speed;
        Direction = direction;
    }
}
