using DefaultEcs;
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
    private const int   TownZoneId  = 1;
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

    private static long _nextEntityId = 1000;

    // 자주 쓰이는 EntitySet을 생성자에서 한 번만 만들어 캐싱한다.
    private readonly EntitySet _entitiesWithId;       // EntityId 보유 엔티티 전체
    private readonly EntitySet _entitiesWithPlayer;   // EntityId + PlayerComponent 보유

    public TownZone() : base(TownZoneId, ZoneType.Town)
    {
        _entitiesWithId     = World.GetEntities().With<EntityIdComponent>().AsSet();
        _entitiesWithPlayer = World.GetEntities().With<EntityIdComponent>().With<PlayerComponent>().AsSet();
    }

    // ── 플레이어 관리 ──────────────────────────────────────────────────────────

    /// <summary>플레이어를 마을에 추가한다. BroadcastSpawn 없음 — AoiSystem이 다음 틱에 처리한다.</summary>
    public Entity AddPlayer(ISession session, long playerId, string playerName)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new PlayerComponent(playerId, playerName));
        entity.Set(new SessionComponent(session));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(0f, 0f, 0f));
        entity.Set(new HealthComponent(100));
        entity.Set(new InterestComponent());  // AOI 관심 영역 추적

        // 공간 해시 그리드에 등록
        AoiGrid.Add(entityId, 0f, 0f);

        Log.Information("Player entered town: {PlayerId} — {PlayerName} (EntityId: {EntityId})",
            playerId, playerName, entityId);

        return entity;
    }

    /// <summary>특정 세션의 현재 위치를 반환한다. 없으면 null.</summary>
    public Vector3? GetPlayerPosition(ISession session)
    {
        foreach (var entity in _entitiesWithPlayer.GetEntities())
        {
            if (!entity.Has<SessionComponent>() || !entity.Has<PositionComponent>()) continue;
            if (entity.Get<SessionComponent>().Session != session) continue;
            var p = entity.Get<PositionComponent>().Position;
            return p;
        }
        return null;
    }

    /// <summary>
    /// 입장 시점의 ViewRadius 이내 엔티티 목록을 반환한다.
    /// 새 플레이어는 제외하며, 반환된 목록으로 InterestComponent를 초기화한다.
    /// </summary>
    public List<EntityInfo> GetNearbyEntityInfos(ISession excludeSession)
    {
        // 새 플레이어 위치 파악 (항상 origin이지만 동적으로 처리)
        float playerX = 0f, playerZ = 0f;
        foreach (var candidate in _entitiesWithId.GetEntities())
        {
            if (!candidate.Has<SessionComponent>() || !candidate.Has<PositionComponent>()) continue;
            if (candidate.Get<SessionComponent>().Session != excludeSession) continue;
            ref var playerPos = ref candidate.Get<PositionComponent>();
            playerX = playerPos.Position.X;
            playerZ = playerPos.Position.Z;
            break;
        }

        var result = new List<EntityInfo>();

        foreach (var entity in _entitiesWithId.GetEntities())
        {
            if (!entity.Has<PositionComponent>() || !entity.Has<HealthComponent>())
                continue;

            // 새로 입장하는 플레이어 본인은 제외
            if (entity.Has<SessionComponent>())
            {
                ref var session = ref entity.Get<SessionComponent>();
                if (session.Session == excludeSession) continue;
            }

            // ViewRadius 거리 필터
            ref var position = ref entity.Get<PositionComponent>();
            float distX = position.Position.X - playerX;
            float distZ = position.Position.Z - playerZ;
            if (distX * distX + distZ * distZ > ViewRadiusSq) continue;

            ref var entityId = ref entity.Get<EntityIdComponent>();
            ref var health   = ref entity.Get<HealthComponent>();
            bool isPlayer    = entity.Has<PlayerComponent>();
            string name      = isPlayer ? entity.Get<PlayerComponent>().Name : string.Empty;

            result.Add(new EntityInfo
            {
                EntityId   = entityId.EntityId,
                EntityType = isPlayer ? GameShared.Proto.EntityType.Player : GameShared.Proto.EntityType.Monster,
                Name       = name,
                Position   = new Vec3 { X = position.Position.X, Y = position.Position.Y, Z = position.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            });
        }

        return result;
    }

    protected override void OnUpdate(float deltaTime) { }

    public override void Dispose()
    {
        _entitiesWithId.Dispose();
        _entitiesWithPlayer.Dispose();
        base.Dispose();
    }
}
