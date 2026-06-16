using Arch.Core;
using Arch.Core.Extensions;
using GameServer.Game.Components;
using GameServer.Network;
using GameShared.Enums;
using GameShared.Proto;
using GameShared.Utils;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// 모든 플레이어가 모이는 싱글톤 마을 존.
/// 안전 지역 — 전투 없음, 채팅과 이동만 가능.
/// </summary>
public class TownZone : Zone
{
    private const int   TownZoneId   = 1;
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

    private static long _nextEntityId = 1000;

    private readonly QueryDescription _allQuery    = new QueryDescription().WithAll<EntityIdComponent>();
    private readonly QueryDescription _playerQuery = new QueryDescription().WithAll<EntityIdComponent, PlayerComponent>();

    public TownZone() : base(TownZoneId, ZoneType.Town) { }

    // ── 플레이어 관리 ──────────────────────────────────────────────────────────

    public Entity AddPlayer(ISession session, long playerId, string playerName)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        var entity = World.Create(
            new EntityIdComponent(entityId),
            new PlayerComponent(playerId, playerName),
            new SessionComponent(session),
            new ZoneComponent(ZoneId, ZoneType),
            new PositionComponent(0f, 0f, 0f),
            new HealthComponent(100),
            new InterestComponent()
        );

        AoiGrid.Add(entityId, 0f, 0f);

        Log.Information("Player entered town: {PlayerId} — {PlayerName} (EntityId: {EntityId})",
            playerId, playerName, entityId);

        return entity;
    }

    public void RemoveEntityById(long entityId)
    {
        EnqueueAction(() =>
        {
            World.Query(in _allQuery, (Entity entity, ref EntityIdComponent eid) =>
            {
                if (eid.EntityId != entityId) return;
                AoiGrid.Remove(entityId);
                World.Destroy(entity);
            });
        });
    }

    public Vector3? GetPlayerPosition(ISession session)
    {
        Vector3? result = null;
        World.Query(in _playerQuery, (Entity entity, ref SessionComponent sess, ref PositionComponent pos) =>
        {
            if (sess.Session == session)
                result = pos.Position;
        });
        return result;
    }

    /// <summary>
    /// 입장 시점의 ViewRadius 이내 엔티티 목록을 반환한다.
    /// </summary>
    public List<EntityInfo> GetNearbyEntityInfos(ISession excludeSession)
    {
        float playerX = 0f, playerZ = 0f;
        World.Query(in _allQuery, (Entity entity, ref EntityIdComponent eid) =>
        {
            if (!entity.Has<SessionComponent>() || !entity.Has<PositionComponent>()) return;
            if (entity.Get<SessionComponent>().Session != excludeSession) return;
            ref var p = ref entity.TryGetRef<PositionComponent>(out _);
            playerX = p.Position.X;
            playerZ = p.Position.Z;
        });

        var result = new List<EntityInfo>();

        World.Query(in _allQuery, (Entity entity, ref EntityIdComponent entityId) =>
        {
            if (!entity.Has<PositionComponent>() || !entity.Has<HealthComponent>()) return;
            if (entity.Has<SessionComponent>() && entity.Get<SessionComponent>().Session == excludeSession) return;

            ref var pos    = ref entity.TryGetRef<PositionComponent>(out _);
            float distX = pos.Position.X - playerX;
            float distZ = pos.Position.Z - playerZ;
            if (distX * distX + distZ * distZ > ViewRadiusSq) return;

            ref var health  = ref entity.TryGetRef<HealthComponent>(out _);
            bool isPlayer   = entity.Has<PlayerComponent>();
            string name     = isPlayer ? entity.Get<PlayerComponent>().Name : string.Empty;

            result.Add(new EntityInfo
            {
                EntityId   = entityId.EntityId,
                EntityType = isPlayer ? GameShared.Proto.EntityType.Player : GameShared.Proto.EntityType.Monster,
                Name       = name,
                Position   = new Vec3 { X = pos.Position.X, Y = pos.Position.Y, Z = pos.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            });
        });

        return result;
    }

    protected override void OnUpdate(float deltaTime) { }
}
