using System.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using GameServer.Database;
using GameServer.Game.Zones;
using GameShared.Data;
using GameShared.Enums;
using GameShared.Generated.Enums;
using GameShared.Proto;
using Serilog;

namespace GameServer.Network;

public partial class PacketHandler
{
    private const float PortalInteractRadius = 5f;

    // ── Town ─────────────────────────────────────────────────────────────────

    [PacketHandler(PacketId.C2S_EnterTown)]
    private void OnEnterTown(ISession session, C2S_EnterTown packet)
    {
        FireAndForget(OnEnterTownAsync(session, packet));
    }

    private async Task OnEnterTownAsync(ISession session, C2S_EnterTown packet)
    {
        Log.Information("Session {Id}: enter town request", session.SessionId);

        if (session.PlayerId == null || string.IsNullOrEmpty(session.PlayerName))
        {
            session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
            return;
        }

        try
        {
            var characters = await DatabaseManager.Instance.Common.GetCharactersByAccountIdAsync(session.PlayerId.Value);
            if (characters.Count == 0)
            {
                Log.Warning("Session {Id}: no characters for account {AccountId}", session.SessionId, session.PlayerId.Value);
                session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
                return;
            }

            var character = characters[0];
            var player    = await DatabaseManager.Instance.Game.GetPlayerByCharacterIdAsync(character.CharacterId);

            if (player == null)
            {
                Log.Information("Session {Id}: creating new player for character {CharacterId}", session.SessionId, character.CharacterId);
                var playerId = await DatabaseManager.Instance.Game.CreatePlayerAsync(
                    character.CharacterId, session.PlayerId.Value,
                    character.CharacterName, character.CharacterClass ?? "Warrior");

                if (playerId == null)
                {
                    session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
                    return;
                }

                player = await DatabaseManager.Instance.Game.GetPlayerByCharacterIdAsync(character.CharacterId);
            }

            if (player == null)
            {
                session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
                return;
            }

            await DatabaseManager.Instance.Game.UpdatePlayerLoginAsync(player.PlayerId);

            var townZone = ZoneManager.Instance.GetTownZone();
            var entity   = townZone.AddPlayer(session, session.PlayerId.Value, player.CharacterName);

            var entityId = entity.Get<Game.Components.EntityIdComponent>().EntityId;
            _sessionToEntityId[session.SessionId] = entityId;
            _sessionToZoneId[session.SessionId]   = townZone.ZoneId;

            var nearbyEntities = townZone.GetNearbyEntityInfos(session);

            var interest = entity.Get<Game.Components.InterestComponent>();
            foreach (var e in nearbyEntities)
                interest.VisibleEntityIds.Add(e.EntityId);

            var result = new S2C_EnterTownResult
            {
                Success  = true,
                EntityId = entityId,
                Position = new Vec3 { X = player.PositionX, Y = player.PositionY, Z = player.PositionZ }
            };
            result.NearbyEntities.AddRange(nearbyEntities);
            session.Send(PacketId.S2C_EnterTownResult, result);

            Log.Information("Session {Id}: entered town (EntityId={EntityId})", session.SessionId, entityId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: enter town failed", session.SessionId);
            session.Send(PacketId.S2C_EnterTownResult, new S2C_EnterTownResult { Success = false });
        }
    }

    // ── Dungeon ───────────────────────────────────────────────────────────────

    [PacketHandler(PacketId.C2S_EnterDungeon)]
    private void OnEnterDungeon(ISession session, C2S_EnterDungeon packet)
    {
        if (session.PlayerId == null || string.IsNullOrEmpty(session.PlayerName))
        {
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Not logged in" });
            return;
        }

        var townZone  = ZoneManager.Instance.GetTownZone();
        var playerPos = townZone.GetPlayerPosition(session);
        if (playerPos == null)
        {
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Player not in town" });
            return;
        }

        if (GameDataManager.Instance.IsLoaded)
        {
            var portals = GameDataManager.MapObjectData
                .Where(o => o.ObjectType == ObjectType.DungeonPortal && o.ReferenceId == packet.DungeonId)
                .ToList();

            bool nearPortal = portals.Any(p =>
            {
                float dx = p.PosX - playerPos.Value.X;
                float dz = p.PosZ - playerPos.Value.Z;
                return MathF.Sqrt(dx * dx + dz * dz) <= PortalInteractRadius;
            });

            if (!nearPortal)
            {
                Log.Warning("Session {Id}: EnterDungeon rejected — not near portal (dungeonId={DungeonId}, pos={X},{Z})",
                    session.SessionId, packet.DungeonId, playerPos.Value.X, playerPos.Value.Z);
                session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                    { Success = false, Message = "Not near dungeon portal" });
                return;
            }
        }

        try
        {
            var dungeonZone = ZoneManager.Instance.CreateDungeonZone(packet.DungeonId);
            var entity      = dungeonZone.AddPlayer(session, session.PlayerId.Value, session.PlayerName);
            var entityId    = entity.Get<Game.Components.EntityIdComponent>().EntityId;

            _sessionToEntityId[session.SessionId] = entityId;
            _sessionToZoneId[session.SessionId]   = dungeonZone.ZoneId;

            var nearbyEntities = dungeonZone.GetNearbyEntityInfos(session);

            var interest  = entity.Get<Game.Components.InterestComponent>();
            var nearbyIds = nearbyEntities.Select(e => e.EntityId).ToList();
            foreach (var id in nearbyIds)
                interest.VisibleEntityIds.Add(id);
            dungeonZone.SubscribeInitialEntities(session, nearbyIds);

            var result = new S2C_EnterDungeonResult
            {
                Success  = true,
                Message  = "Entered dungeon",
                EntityId = entityId,
                Position = new Vec3 { X = 0, Y = 0, Z = 0 }
            };
            result.NearbyEntities.AddRange(nearbyEntities);
            session.Send(PacketId.S2C_EnterDungeonResult, result);

            Log.Information("Session {Id}: entered dungeon (EntityId={EntityId}, ZoneId={ZoneId})",
                session.SessionId, entityId, dungeonZone.ZoneId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: enter dungeon failed", session.SessionId);
            session.Send(PacketId.S2C_EnterDungeonResult, new S2C_EnterDungeonResult
                { Success = false, Message = "Failed to enter dungeon" });
        }
    }

    [PacketHandler(PacketId.C2S_LeaveDungeon)]
    private void OnLeaveDungeon(ISession session, C2S_LeaveDungeon packet)
    {
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: LeaveDungeon but not in any zone", session.SessionId);
            return;
        }

        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone is not DungeonZone dungeonZone)
        {
            Log.Warning("Session {Id}: LeaveDungeon but zone {ZoneId} is not a dungeon", session.SessionId, zoneId);
            return;
        }

        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
            return;

        dungeonZone.RemovePlayer(session.PlayerId ?? 0);

        _sessionToEntityId.Remove(session.SessionId);
        _sessionToZoneId.Remove(session.SessionId);

        Log.Information("Session {Id}: left dungeon (ZoneId={ZoneId})", session.SessionId, zoneId);

        FireAndForget(OnEnterTownAsync(session, new C2S_EnterTown()));

        if (dungeonZone.PartyMembers.Count == 0)
        {
            Log.Information("Dungeon ZoneId={ZoneId} is empty, destroying", zoneId);
            Task.Run(() => ZoneManager.Instance.RemoveZone(zoneId));
        }
    }

    // ── Field ─────────────────────────────────────────────────────────────────

    [PacketHandler(PacketId.C2S_EnterField)]
    private void OnEnterField(ISession session, C2S_EnterField packet)
    {
        FireAndForget(OnEnterFieldAsync(session, packet));
    }

    private async Task OnEnterFieldAsync(ISession session, C2S_EnterField packet)
    {
        if (session.PlayerId == null || string.IsNullOrEmpty(session.PlayerName))
        {
            session.Send(PacketId.S2C_EnterFieldResult, new S2C_EnterFieldResult
                { Success = false, Message = "Not logged in" });
            return;
        }

        var fieldZone = ZoneManager.Instance.GetFieldZone(packet.FieldId);
        if (fieldZone == null)
        {
            session.Send(PacketId.S2C_EnterFieldResult, new S2C_EnterFieldResult
                { Success = false, Message = $"Field {packet.FieldId} not found" });
            return;
        }

        var (dailyRemaining, weeklyRemaining) = await fieldZone.LoadQuotaAsync(session.PlayerId.Value);

        if (dailyRemaining <= 0)
        {
            session.Send(PacketId.S2C_EnterFieldResult, new S2C_EnterFieldResult
                { Success = false, Message = "일일 입장 가능 시간이 소진되었습니다." });
            return;
        }
        if (weeklyRemaining <= 0)
        {
            session.Send(PacketId.S2C_EnterFieldResult, new S2C_EnterFieldResult
                { Success = false, Message = "주간 입장 가능 시간이 소진되었습니다." });
            return;
        }

        if (_sessionToZoneId.TryGetValue(session.SessionId, out var oldZoneId))
        {
            var oldZone = ZoneManager.Instance.GetZone(oldZoneId);
            if (oldZone is TownZone townZoneOld)
            {
                if (_sessionToEntityId.TryGetValue(session.SessionId, out var oldEntityId))
                    townZoneOld.RemoveEntityById(oldEntityId);
            }
        }

        var entity   = fieldZone.AddPlayer(session, session.PlayerId.Value, session.PlayerName);
        var entityId = entity.Get<Game.Components.EntityIdComponent>().EntityId;

        _sessionToEntityId[session.SessionId] = entityId;
        _sessionToZoneId[session.SessionId]   = fieldZone.ZoneId;

        var nearbyEntities = fieldZone.GetNearbyEntityInfos(session);

        var interest  = entity.Get<Game.Components.InterestComponent>();
        var nearbyIds = nearbyEntities.Select(e => e.EntityId).ToList();
        foreach (var id in nearbyIds)
            interest.VisibleEntityIds.Add(id);
        fieldZone.SubscribeInitialEntities(session, nearbyIds);

        var result = new S2C_EnterFieldResult
        {
            Success                = true,
            Message                = "Entered field",
            EntityId               = entityId,
            Position               = new Vec3 { X = 0, Y = 0, Z = 0 },
            DailyRemainingSeconds  = dailyRemaining,
            WeeklyRemainingSeconds = weeklyRemaining
        };
        result.NearbyEntities.AddRange(nearbyEntities);
        session.Send(PacketId.S2C_EnterFieldResult, result);

        foreach (var objInfo in fieldZone.GetAllObjectInfos())
            session.Send(PacketId.S2C_ObjectInfo, objInfo);

        Log.Information("Session {Id}: entered field (EntityId={EntityId}, FieldId={FieldId}, DailyRemaining={Daily}s)",
            session.SessionId, entityId, packet.FieldId, dailyRemaining);
    }

    [PacketHandler(PacketId.C2S_LeaveField)]
    private void OnLeaveField(ISession session, C2S_LeaveField packet)
    {
        FireAndForget(OnLeaveFieldAsync(session));
    }

    private async Task OnLeaveFieldAsync(ISession session)
    {
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: LeaveField but not in any zone", session.SessionId);
            return;
        }

        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone is not TimeLimitedFieldZone fieldZone)
        {
            Log.Warning("Session {Id}: LeaveField but zone {ZoneId} is not a field", session.SessionId, zoneId);
            return;
        }

        await fieldZone.RemovePlayerAsync(session.PlayerId ?? 0);

        _sessionToEntityId.Remove(session.SessionId);
        _sessionToZoneId.Remove(session.SessionId);

        Log.Information("Session {Id}: left field (ZoneId={ZoneId})", session.SessionId, zoneId);

        FireAndForget(OnEnterTownAsync(session, new C2S_EnterTown()));
    }

    // ── Movement / Chat ───────────────────────────────────────────────────────

    [PacketHandler(PacketId.C2S_Move)]
    private void OnMove(ISession session, C2S_Move packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
        {
            Log.Warning("Session {Id}: move but no entity", session.SessionId);
            return;
        }

        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: move but no zone", session.SessionId);
            return;
        }

        var dest = packet.Destination;
        if (dest == null || !IsValidCoordinate(dest.X, dest.Y, dest.Z))
        {
            Log.Warning("Session {Id}: invalid move destination", session.SessionId);
            return;
        }

        Log.Debug("Session {Id}: move → ({X},{Y},{Z})", session.SessionId, dest.X, dest.Y, dest.Z);

        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone == null) return;

        var destination = new GameShared.Utils.Vector3 { X = dest.X, Y = dest.Y, Z = dest.Z };
        zone.HandleMove(entityId, destination, 5.0f);
    }

    [PacketHandler(PacketId.C2S_Chat)]
    private void OnChat(ISession session, C2S_Chat packet)
    {
        if (!_sessionToEntityId.TryGetValue(session.SessionId, out var entityId))
        {
            Log.Warning("Session {Id}: chat but no entity", session.SessionId);
            return;
        }

        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId))
        {
            Log.Warning("Session {Id}: chat but no zone", session.SessionId);
            return;
        }

        var message = packet.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            return;

        if (message.Length > MaxChatLength)
        {
            Log.Warning("Session {Id}: chat message too long ({Len}), ignoring", session.SessionId, message.Length);
            return;
        }

        Log.Information("Session {Id}: chat — {Message}", session.SessionId, message);

        var zone = ZoneManager.Instance.GetZone(zoneId);
        zone?.HandleChat(entityId, message);
    }

    // ── Respawn ───────────────────────────────────────────────────────────────

    [PacketHandler(PacketId.C2S_Respawn)]
    private void OnRespawn(ISession session, C2S_Respawn packet)
    {
        if (!_sessionToZoneId.TryGetValue(session.SessionId, out var zoneId)) return;
        var zone = ZoneManager.Instance.GetZone(zoneId);
        if (zone is not TimeLimitedFieldZone fieldZone) return;

        fieldZone.RequestRespawn(session.PlayerId ?? 0);
    }
}
