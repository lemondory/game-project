using DefaultEcs;
using DefaultEcs.System;
using GameServer.Database;
using GameServer.Game.Components;
using GameServer.Game.Systems;
using GameServer.Network;
using GameShared.Data;
using GameShared.Enums;
using GameShared.Generated.Data;
using GameShared.Proto;
using GameShared.Utils;
using Google.Protobuf;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// 시간제 사냥터 존 (영구 공유 존).
/// - 서버 시작 시 생성되어 계속 유지된다 (인스턴스 아님).
/// - 플레이어별 일간/주간 쿼터를 소진하면 강제 퇴장.
/// - 몬스터는 사망 후 RespawnDelaySeconds만큼 대기 후 재등장.
/// - 클리어 개념 없음.
/// </summary>
public class TimeLimitedFieldZone : Zone
{
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

    // 쿼터 DB 저장 주기 (초) — 1분마다 저장해 서버 크래시 시 손실 최소화
    private const float QuotaSaveInterval      = 60f;
    // 클라이언트 쿼터 업데이트 브로드캐스트 주기 (초)
    private const float QuotaBroadcastInterval = 60f;

    private static long _nextEntityId = 50000; // 필드 엔티티 ID 범위

    public int FieldId { get; }

    private readonly TimeLimitedFieldData _fieldData;
    private readonly Random _random = new();

    // 리스폰 대기 큐 (monsterId, 스폰 위치, 남은 대기 시간)
    private readonly List<(int monsterId, Vector3 position, float delay)> _respawnQueue = new();

    // 플레이어별 쿼터 인메모리 캐시
    // key: playerId
    private readonly Dictionary<long, PlayerQuotaState> _quotaCache = new();

    // 마지막 DB 저장/브로드캐스트 시각
    private float _quotaSaveCooldown    = QuotaSaveInterval;
    private float _quotaBroadcastCooldown = QuotaBroadcastInterval;

    // ── 플레이어 쿼터 상태 (인메모리) ───────────────────────────────────────
    private class PlayerQuotaState
    {
        public long PlayerId          { get; init; }
        public int  DailyUsedSeconds  { get; set; }
        public int  WeeklyUsedSeconds { get; set; }
        public DateTime LastDailyReset  { get; set; }
        public DateTime LastWeeklyReset { get; set; }

        // 일간/주간 리셋을 체크하고 필요하면 초기화한다.
        public void ApplyResets()
        {
            var today = DateTime.UtcNow.Date;
            var thisWeek = GetWeekStart(DateTime.UtcNow);

            if (LastDailyReset.Date < today)
            {
                DailyUsedSeconds = 0;
                LastDailyReset   = today;
            }
            if (LastWeeklyReset.Date < thisWeek)
            {
                WeeklyUsedSeconds = 0;
                LastWeeklyReset   = thisWeek;
            }
        }

        private static DateTime GetWeekStart(DateTime dt)
        {
            // ISO 8601: 주 시작은 월요일
            int diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return dt.Date.AddDays(-diff);
        }
    }

    public TimeLimitedFieldZone(int zoneId, int fieldId) : base(zoneId, ZoneType.Field)
    {
        FieldId = fieldId;
        _fieldData = GameDataManager.TimeLimitedFieldData.GetById(fieldId)
            ?? throw new ArgumentException($"Unknown fieldId={fieldId}");
    }

    protected override ISystem<float> CreateSystems()
    {
        return new SequentialSystem<float>(
            new MonsterAISystem(World),
            new MovementSystem(World),
            new AoiSystem(World, AoiGrid, SubscriberMap),
            new CombatSystem(World,
                onAttack: BroadcastAttack,
                onDamage: (targetId, damage, hp, maxHp) => BroadcastDamage(targetId, damage, hp, maxHp),
                onDeath:  HandleDeath),
            new BroadcastSystem(World, SubscriberMap),
            new SessionSystem(World, AoiGrid)
        );
    }

    // ── 플레이어 관리 ────────────────────────────────────────────────────────

    /// <summary>
    /// 쿼터를 DB에서 로드하고 잔여 시간을 반환한다.
    /// 쿼터 소진 시 (dailyRemaining=0 OR weeklyRemaining=0) 입장 거부용.
    /// </summary>
    public async Task<(int dailyRemaining, int weeklyRemaining)> LoadQuotaAsync(long playerId)
    {
        var dbQuota = await DatabaseManager.Instance.Game.GetFieldQuotaAsync(playerId, FieldId);

        var state = new PlayerQuotaState
        {
            PlayerId          = playerId,
            DailyUsedSeconds  = dbQuota?.DailyUsedSeconds  ?? 0,
            WeeklyUsedSeconds = dbQuota?.WeeklyUsedSeconds ?? 0,
            LastDailyReset    = dbQuota?.LastDailyReset    ?? DateTime.UtcNow.Date,
            LastWeeklyReset   = dbQuota?.LastWeeklyReset   ?? GetWeekStart(DateTime.UtcNow),
        };

        state.ApplyResets();
        _quotaCache[playerId] = state;

        int dailyRemaining  = _fieldData.DailyLimitMinutes  * 60 - state.DailyUsedSeconds;
        int weeklyRemaining = _fieldData.WeeklyLimitMinutes * 60 - state.WeeklyUsedSeconds;
        return (Math.Max(0, dailyRemaining), Math.Max(0, weeklyRemaining));
    }

    public Entity AddPlayer(ISession session, long playerId, string playerName)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        const int defaultClassId = 1;
        var classData = GameDataManager.CharacterClassData.GetById(defaultClassId);
        int hp      = classData?.BaseHp      ?? 120;
        int attack  = classData?.BaseAttack  ?? 10;
        int defense = classData?.BaseDefense ?? 8;

        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new PlayerComponent(playerId, playerName, level: 1, exp: 0, gold: 0, classId: defaultClassId));
        entity.Set(new SessionComponent(session));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(0f, 0f, 0f));
        entity.Set(new HealthComponent(hp));
        entity.Set(new AttackComponent(attack, 3f, 1f));
        entity.Set(new DefenseComponent(defense));
        entity.Set(new InterestComponent());

        AoiGrid.Add(entityId, 0f, 0f);

        Log.Information("Player entered field: {PlayerId} - {PlayerName} (EntityId={EntityId}, FieldId={FieldId})",
            playerId, playerName, entityId, FieldId);

        return entity;
    }

    public async Task RemovePlayerAsync(long playerId)
    {
        // 쿼터 즉시 DB 저장
        await SavePlayerQuotaAsync(playerId);
        _quotaCache.Remove(playerId);

        // ECS에서 제거
        var playerEntities = World.GetEntities()
            .With<PlayerComponent>()
            .With<EntityIdComponent>()
            .AsSet();

        foreach (var entity in playerEntities.GetEntities())
        {
            if (!entity.IsAlive) continue;
            if (entity.Get<PlayerComponent>().PlayerId != playerId) continue;

            ref var eid = ref entity.Get<EntityIdComponent>();
            AoiGrid.Remove(eid.EntityId);
            SubscriberMap.RemoveEntity(eid.EntityId);
            entity.Dispose();
            break;
        }

        Log.Information("Player {PlayerId} left field ZoneId={ZoneId}", playerId, ZoneId);
    }

    // ── 몬스터 스폰 ──────────────────────────────────────────────────────────

    public Entity SpawnMonster(int monsterId, Vector3 position)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);
        var monsterData = GameDataManager.MonsterData.GetById(monsterId);
        if (monsterData == null) return default;

        var entity = World.CreateEntity();
        entity.Set(new EntityIdComponent(entityId));
        entity.Set(new MonsterComponent(monsterId, monsterData));
        entity.Set(new ZoneComponent(ZoneId, ZoneType));
        entity.Set(new PositionComponent(position));
        entity.Set(new HealthComponent(monsterData.Hp));
        entity.Set(new AttackComponent(monsterData.AttackPower, monsterData.AttackRange, monsterData.AttackCooldown));
        entity.Set(new DefenseComponent(monsterData.Defense));
        entity.Set(new AIComponent(monsterData.AggroRange, monsterData.AttackRange, monsterData.MoveSpeed));
        entity.Set(new CombatStateComponent(false, 0));

        AoiGrid.Add(entityId, position.X, position.Z);
        return entity;
    }

    // ── 플레이어 공격 처리 ───────────────────────────────────────────────────

    public void HandleAttack(long attackerEntityId, long targetEntityId, float currentTime)
    {
        var entities = World.GetEntities().With<EntityIdComponent>().With<AttackComponent>().AsSet();

        Entity? attackerEntity = null;
        foreach (var e in entities.GetEntities())
        {
            if (e.Get<EntityIdComponent>().EntityId == attackerEntityId) { attackerEntity = e; break; }
        }
        if (!attackerEntity.HasValue) return;

        ref var attack = ref attackerEntity.Value.Get<AttackComponent>();
        if (!attack.CanAttack(currentTime)) return;

        Entity? targetEntity = null;
        foreach (var e in entities.GetEntities())
        {
            if (e.Get<EntityIdComponent>().EntityId == targetEntityId) { targetEntity = e; break; }
        }
        if (!targetEntity.HasValue) return;

        ref var attackerPos = ref attackerEntity.Value.Get<PositionComponent>();
        ref var targetPos   = ref targetEntity.Value.Get<PositionComponent>();
        if (attackerPos.Position.Distance(targetPos.Position) > attack.Range) return;

        ref var targetHealth  = ref targetEntity.Value.Get<HealthComponent>();
        int targetDefense = targetEntity.Value.Has<DefenseComponent>()
            ? targetEntity.Value.Get<DefenseComponent>().Defense : 0;
        int damage = Math.Max(1, attack.Power - targetDefense);

        targetHealth.Current  = Math.Max(0, targetHealth.Current - damage);
        attack.LastAttackTime = currentTime;

        BroadcastAttack(attackerEntityId, targetEntityId);
        BroadcastDamage(targetEntityId, damage, targetHealth.Current, targetHealth.Max);

        if (targetHealth.IsDead)
            HandleDeath(targetEntity.Value, attackerEntity.Value);
    }

    // ── 사망 / 리스폰 ────────────────────────────────────────────────────────

    private void HandleDeath(Entity deadEntity, Entity killerEntity)
    {
        ref var deadEntityId   = ref deadEntity.Get<EntityIdComponent>();
        ref var killerEntityId = ref killerEntity.Get<EntityIdComponent>();

        if (deadEntity.Has<MonsterComponent>() && killerEntity.Has<PlayerComponent>())
            GiveReward(deadEntity, killerEntity);

        BroadcastDeath(deadEntityId.EntityId, killerEntityId.EntityId);

        if (deadEntity.Has<MonsterComponent>())
        {
            ref var monsterComp = ref deadEntity.Get<MonsterComponent>();
            var deadPos = deadEntity.Get<PositionComponent>().Position;

            AoiGrid.Remove(deadEntityId.EntityId);
            SubscriberMap.RemoveEntity(deadEntityId.EntityId);
            deadEntity.Dispose();

            // 리스폰 큐 등록
            _respawnQueue.Add((monsterComp.MonsterId, deadPos, _fieldData.RespawnDelaySeconds));
        }
    }

    private void GiveReward(Entity deadEntity, Entity killerEntity)
    {
        if (!killerEntity.Has<SessionComponent>()) return;

        ref var monster = ref deadEntity.Get<MonsterComponent>();
        ref var player  = ref killerEntity.Get<PlayerComponent>();
        ref var session = ref killerEntity.Get<SessionComponent>();

        player.Exp  += monster.Data.ExpReward;
        player.Gold += monster.Data.GoldReward;

        int expForNextLevel = player.Level * 100;
        if (player.Exp >= expForNextLevel)
        {
            player.Level++;
            player.Exp -= expForNextLevel;

            var classData = GameDataManager.CharacterClassData.GetById(player.ClassId);
            if (classData != null
                && killerEntity.Has<HealthComponent>()
                && killerEntity.Has<AttackComponent>()
                && killerEntity.Has<DefenseComponent>())
            {
                ref var health = ref killerEntity.Get<HealthComponent>();
                ref var atk    = ref killerEntity.Get<AttackComponent>();
                ref var def    = ref killerEntity.Get<DefenseComponent>();

                health.Max     += classData.HpPerLevel;
                health.Current += classData.HpPerLevel;
                atk.Power      += classData.AttackPerLevel;
                def.Defense    += classData.DefensePerLevel;

                session.Session.Send(PacketId.S2C_LevelUp, new S2C_LevelUp
                {
                    NewLevel   = player.Level,
                    NewMaxHp   = health.Max,
                    NewAttack  = atk.Power,
                    NewDefense = def.Defense
                });
            }
        }

        session.Session.Send(PacketId.S2C_RewardResult, new S2C_RewardResult
        {
            ExpReward  = monster.Data.ExpReward,
            GoldReward = monster.Data.GoldReward,
            TotalExp   = player.Exp,
            TotalGold  = player.Gold
        });
    }

    // ── 게임 루프 훅 ─────────────────────────────────────────────────────────

    protected override void OnUpdate(float deltaTime)
    {
        ProcessRespawnQueue(deltaTime);
        ProcessQuotaTracking(deltaTime);
    }

    private void ProcessRespawnQueue(float deltaTime)
    {
        for (int i = _respawnQueue.Count - 1; i >= 0; i--)
        {
            var (monsterId, pos, delay) = _respawnQueue[i];
            float newDelay = delay - deltaTime;
            if (newDelay <= 0f)
            {
                _respawnQueue.RemoveAt(i);
                SpawnMonster(monsterId, pos);
            }
            else
            {
                _respawnQueue[i] = (monsterId, pos, newDelay);
            }
        }
    }

    private void ProcessQuotaTracking(float deltaTime)
    {
        if (_quotaCache.Count == 0) return;

        // 접속 중인 모든 플레이어 쿼터를 deltaTime만큼 소진
        var playersToEvict = new List<(long playerId, ISession session)>();

        var playerEntities = World.GetEntities()
            .With<PlayerComponent>()
            .With<SessionComponent>()
            .AsSet();

        foreach (var entity in playerEntities.GetEntities())
        {
            if (!entity.IsAlive) continue;
            ref var player  = ref entity.Get<PlayerComponent>();
            ref var session = ref entity.Get<SessionComponent>();

            if (!_quotaCache.TryGetValue(player.PlayerId, out var state)) continue;

            state.ApplyResets();
            state.DailyUsedSeconds  += (int)deltaTime;
            state.WeeklyUsedSeconds += (int)deltaTime;

            int dailyLimit  = _fieldData.DailyLimitMinutes  * 60;
            int weeklyLimit = _fieldData.WeeklyLimitMinutes * 60;

            // 쿼터 소진 시 퇴장 대상으로 수집
            if (state.DailyUsedSeconds >= dailyLimit || state.WeeklyUsedSeconds >= weeklyLimit)
                playersToEvict.Add((player.PlayerId, session.Session));
        }

        // 쿼터 소진 플레이어 강제 퇴장
        foreach (var (playerId, sess) in playersToEvict)
        {
            sess.Send(PacketId.S2C_FieldQuotaUpdate, new S2C_FieldQuotaUpdate
            {
                DailyRemainingSeconds  = 0,
                WeeklyRemainingSeconds = 0
            });
            sess.Send(PacketId.S2C_LeaveField, new S2C_LeaveField { Success = true });
            _ = RemovePlayerAsync(playerId);
        }

        // 1분마다 DB 저장 + 클라이언트 쿼터 업데이트 브로드캐스트
        _quotaSaveCooldown      -= deltaTime;
        _quotaBroadcastCooldown -= deltaTime;

        if (_quotaSaveCooldown <= 0f)
        {
            _quotaSaveCooldown = QuotaSaveInterval;
            foreach (var playerId in _quotaCache.Keys.ToList())
                _ = SavePlayerQuotaAsync(playerId);
        }

        if (_quotaBroadcastCooldown <= 0f)
        {
            _quotaBroadcastCooldown = QuotaBroadcastInterval;
            BroadcastQuotaUpdate();
        }
    }

    private void BroadcastQuotaUpdate()
    {
        var playerEntities = World.GetEntities()
            .With<PlayerComponent>()
            .With<SessionComponent>()
            .AsSet();

        int dailyLimit  = _fieldData.DailyLimitMinutes  * 60;
        int weeklyLimit = _fieldData.WeeklyLimitMinutes * 60;

        foreach (var entity in playerEntities.GetEntities())
        {
            if (!entity.IsAlive) continue;
            ref var player  = ref entity.Get<PlayerComponent>();
            ref var session = ref entity.Get<SessionComponent>();

            if (!_quotaCache.TryGetValue(player.PlayerId, out var state)) continue;

            session.Session.Send(PacketId.S2C_FieldQuotaUpdate, new S2C_FieldQuotaUpdate
            {
                DailyRemainingSeconds  = Math.Max(0, dailyLimit  - state.DailyUsedSeconds),
                WeeklyRemainingSeconds = Math.Max(0, weeklyLimit - state.WeeklyUsedSeconds)
            });
        }
    }

    private async Task SavePlayerQuotaAsync(long playerId)
    {
        if (!_quotaCache.TryGetValue(playerId, out var state)) return;

        await DatabaseManager.Instance.Game.SaveFieldQuotaAsync(
            playerId, FieldId,
            state.DailyUsedSeconds, state.WeeklyUsedSeconds,
            state.LastDailyReset, state.LastWeeklyReset);
    }

    // ── 입장 시 주변 엔티티 정보 ─────────────────────────────────────────────

    public List<EntityInfo> GetNearbyEntityInfos(ISession excludeSession)
    {
        float playerX = 0f, playerZ = 0f;
        var allEntities = World.GetEntities()
            .With<EntityIdComponent>().With<PositionComponent>().With<HealthComponent>().AsSet();

        foreach (var candidate in allEntities.GetEntities())
        {
            if (!candidate.Has<SessionComponent>()) continue;
            if (candidate.Get<SessionComponent>().Session != excludeSession) continue;
            ref var pos = ref candidate.Get<PositionComponent>();
            playerX = pos.Position.X; playerZ = pos.Position.Z;
            break;
        }

        var result = new List<EntityInfo>();
        foreach (var entity in allEntities.GetEntities())
        {
            if (entity.Has<SessionComponent>())
            {
                ref var session = ref entity.Get<SessionComponent>();
                if (session.Session == excludeSession) continue;
            }

            ref var p = ref entity.Get<PositionComponent>();
            float dx = p.Position.X - playerX, dz = p.Position.Z - playerZ;
            if (dx * dx + dz * dz > ViewRadiusSq) continue;

            ref var entityId = ref entity.Get<EntityIdComponent>();
            ref var health   = ref entity.Get<HealthComponent>();

            string entityName;
            GameShared.Proto.EntityType entityType;

            if (entity.Has<PlayerComponent>())
            {
                ref var pl = ref entity.Get<PlayerComponent>();
                entityName = pl.Name; entityType = GameShared.Proto.EntityType.Player;
            }
            else if (entity.Has<MonsterComponent>())
            {
                ref var m = ref entity.Get<MonsterComponent>();
                entityName = m.Data.Name; entityType = GameShared.Proto.EntityType.Monster;
            }
            else continue;

            result.Add(new EntityInfo
            {
                EntityId   = entityId.EntityId,
                EntityType = entityType,
                Name       = entityName,
                Position   = new Vec3 { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            });
        }
        return result;
    }

    // ── 브로드캐스트 헬퍼 ────────────────────────────────────────────────────

    private void BroadcastAttack(long attackerEntityId, long targetEntityId)
        => BroadcastToAllPlayers(PacketId.S2C_Attack, new S2C_Attack
            { AttackerEntityId = attackerEntityId, TargetEntityId = targetEntityId });

    private void BroadcastDamage(long targetEntityId, int damage, int currentHp, int maxHp)
        => BroadcastToAllPlayers(PacketId.S2C_Damage, new S2C_Damage
            { TargetEntityId = targetEntityId, Damage = damage, CurrentHp = currentHp, MaxHp = maxHp });

    private void BroadcastDeath(long deadEntityId, long killerEntityId)
    {
        var packet = new S2C_Death { EntityId = deadEntityId, KillerEntityId = killerEntityId };
        var entities = World.GetEntities()
            .With<SessionComponent>().With<InterestComponent>().With<EntityIdComponent>().AsSet();

        foreach (var entity in entities.GetEntities())
        {
            ref var entityId = ref entity.Get<EntityIdComponent>();
            var interest = entity.Get<InterestComponent>();
            if (entityId.EntityId != deadEntityId && !interest.VisibleEntityIds.Contains(deadEntityId)) continue;
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(PacketId.S2C_Death, packet);
        }
    }

    private void BroadcastToAllPlayers(PacketId packetId, IMessage packet)
    {
        var entities = World.GetEntities().With<SessionComponent>().AsSet();
        foreach (var entity in entities.GetEntities())
        {
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(packetId, packet);
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    private static DateTime GetWeekStart(DateTime dt)
    {
        int diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return dt.Date.AddDays(-diff);
    }
}
