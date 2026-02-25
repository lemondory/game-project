using GameShared.Utils;

namespace GameServer.Game.Components;

/// <summary>
/// Target destination for movement
/// </summary>
public struct DestinationComponent
{
    public Vector3 Target;
    public float ArrivalThreshold; // Distance to consider "arrived"

    public DestinationComponent(Vector3 target, float arrivalThreshold = 0.1f)
    {
        Target = target;
        ArrivalThreshold = arrivalThreshold;
    }
}
