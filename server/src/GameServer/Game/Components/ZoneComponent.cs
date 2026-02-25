using GameShared.Enums;

namespace GameServer.Game.Components;

/// <summary>
/// Zone membership
/// </summary>
public struct ZoneComponent
{
    public int ZoneId;
    public ZoneType ZoneType;

    public ZoneComponent(int zoneId, ZoneType zoneType)
    {
        ZoneId = zoneId;
        ZoneType = zoneType;
    }
}
