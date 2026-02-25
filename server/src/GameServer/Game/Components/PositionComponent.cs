using GameShared.Utils;

namespace GameServer.Game.Components;

/// <summary>
/// Entity position in 3D space
/// </summary>
public struct PositionComponent
{
    public Vector3 Position;

    public PositionComponent(Vector3 position)
    {
        Position = position;
    }

    public PositionComponent(float x, float y, float z)
    {
        Position = new Vector3 { X = x, Y = y, Z = z };
    }
}
