using GameServer.Game.Zones;
using GameShared.Enums;
using GameShared.Proto;
using Serilog;

namespace GameServer.Network;

public partial class PacketHandler
{
    [PacketHandler(PacketId.C2S_Attack)]
    private void OnAttack(ISession session, C2S_Attack packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId)) return;
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId)) return;

        Log.Information("Session {Id}: attack target {TargetId}", session.SessionId, packet.TargetEntityId);

        var zone        = ZoneManager.Instance.GetZone(zoneId);
        var currentTime = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;

        if (zone is DungeonZone dungeonZone)
            dungeonZone.HandleAttack(entityId, packet.TargetEntityId, currentTime);
        else if (zone is TimeLimitedFieldZone fieldZone)
            fieldZone.HandleAttack(entityId, packet.TargetEntityId, currentTime);
        else
            Log.Warning("Session {Id}: attack in non-combat zone", session.SessionId);
    }
}
