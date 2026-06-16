using GameServer.Game.Zones;
using GameShared.Enums;
using GameShared.Proto;

namespace GameServer.Network;

public partial class PacketHandler
{
    [PacketHandler(PacketId.C2S_Interact)]
    private void OnInteract(ISession session, C2S_Interact packet)
    {
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId)) return;
        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone is not TimeLimitedFieldZone fieldZone) return;

        fieldZone.RequestInteract(session, session.PlayerId ?? 0, packet.TargetObjectId);
    }
}
