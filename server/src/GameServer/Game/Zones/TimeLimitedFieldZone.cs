using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
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

    private const float QuotaSaveInterval      = 60f;
    private const float QuotaBroadcastInterval = 60f;

    private static long _nextEntityId = 50000;

    public int FieldId { get; }

    private readonly TimeLimitedFieldData _fieldData;
    private readonly Random _random = new();

    private readonly List<WorldObject> _worldObjects = new();
    private int _nextWorldObjectId = 1;

    private readonly List<(int monsterId, Vector3 position, float delay)> _respawnQueue   = new();
    private readonly HashSet<long>                                          _deadPlayerIds  = new();
    private readonly Dictionary<long, PlayerQuotaState>                    _quotaCache     = new();

    private float _quotaSaveCooldown      = QuotaSaveInterval;
    private float _quotaBroadcastCooldown = QuotaBroadcastInterval;

    // 공통 쿼리
    private readonly QueryDescription _allQuery        = new QueryDescription().WithAll<EntityIdComponent, PositionComponent, HealthComponent>();
    private readonly QueryDescription _playerQuery     = new QueryDescription().WithAll<PlayerComponent, EntityIdComponent>();
    private readonly QueryDescription _sessionQuery    = new QueryDescription().WithAll<SessionComponent>();
    private readonly QueryDescription _playerSessQuery = new QueryDescription().WithAll<PlayerComponent, SessionComponent>();
    private readonly QueryDescription _deathBcastQuery = new QueryDescription().WithAll<SessionComponent, InterestComponent, EntityIdComponent>();
    private readonly QueryDescription _attackQuery     = new QueryDescription().WithAll<EntityIdComponent, AttackComponent, PositionComponent>();
    private readonly QueryDescription _respawnQuery    = new QueryDescription()
        .WithAll<PlayerComponent, EntityIdComponent, HealthComponent, PositionComponent, SessionComponent>();

    // ── 플레이어 쿼터 상태 (인메모리) ────────────────────────────────────────
    private class PlayerQuotaState
    {
        public long     PlayerId          { get; init; }
        public int      DailyUsedSeconds  { get; set; }
        public int      WeeklyUsedSeconds { get; set; }
        public DateTime LastDailyReset    { get; set; }
        public DateTime LastWeeklyReset   { get; set; }

        public void ApplyResets()
        {
            var today    = DateTime.UtcNow.Date;
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
            int diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return dt.Date.AddDays(-diff);
        }
    }

    public TimeLimitedFieldZone(int zoneId, int fieldId) : base(zoneId, ZoneType.Field)
    {
        FieldId = fieldId;
        _fieldData = GameDataManager.TimeLimitedFieldData.GetById(fieldId)
            ?? throw new ArgumentException($"Unknown fieldId={fieldId}");
        InitializeWorldObjects();
    }

    private void InitializeWorldObjects()
    {
        var layouts = GameDataManager.WorldObjectLayout.Where(l => l.FieldId == FieldId);
        foreach (var layout in layouts)
        {
            var data = GameDataManager.WorldObjectData.GetById(layout.ObjectDataId);
            if (data == null)
            {
                Log.Warning("WorldObjectLayout {Id}: ObjectDataId={DataId} not found", layout.LayoutId, layout.ObjectDataId);
                continue;
            }
            var pos = new Vector3(layout.PosX, layout.PosY, layout.PosZ);
            _worldObjects.Add(new WorldObject(_nextWorldObjectId++, data, pos));
        }
        Log.Information("FieldZone {FieldId}: {Count} world objects initialized", FieldId, _worldObjects.Count);
    }

    protected override ISystem<float> CreateSystems()
    {
        return new Group<float>($"Field-{ZoneId}",
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

        var entity = World.Create(
            new EntityIdComponent(entityId),
            new PlayerComponent(playerId, playerName, level: 1, exp: 0, gold: 0, classId: defaultClassId),
            new SessionComponent(session),
            new ZoneComponent(ZoneId, ZoneType),
            new PositionComponent(0f, 0f, 0f),
            new HealthComponent(hp),
            new AttackComponent(attack, 3f, 1f),
            new DefenseComponent(defense),
            new InterestComponent()
        );

        AoiGrid.Add(entityId, 0f, 0f);

        Log.Information("Player entered field: {PlayerId} - {PlayerName} (EntityId={EntityId}, FieldId={FieldId})",
            playerId, playerName, entityId, FieldId);

        return entity;
    }

    public async Task RemovePlayerAsync(long playerId)
    {
        _deadPlayerIds.Remove(playerId);

        await SavePlayerQuotaAsync(playerId);
        _quotaCache.Remove(playerId);

        Entity? toRemove = null;
        World.Query(in _playerQuery, (Entity entity, ref PlayerComponent player, ref EntityIdComponent eid) =>
        {
            if (player.PlayerId == playerId) toRemove = entity;
        });

        if (toRemove.HasValue && toRemove.Value.IsAlive())
        {
            var eid = World.Get<EntityIdComponent>(toRemove.Value).EntityId;
            AoiGrid.Remove(eid);
            SubscriberMap.RemoveEntity(eid);
            World.Destroy(toRemove.Value);
        }

        Log.Information("Player {PlayerId} left field ZoneId={ZoneId}", playerId, ZoneId);
    }

    // ── 몬스터 스폰 ──────────────────────────────────────────────────────────

    public Entity SpawnMonster(int monsterId, Vector3 position)
    {
        var entityId    = Interlocked.Increment(ref _nextEntityId);
        var monsterData = GameDataManager.MonsterData.GetById(monsterId);
        if (monsterData == null) return default;

        var entity = World.Create(
            new EntityIdComponent(entityId),
            new MonsterComponent(monsterId, monsterData),
            new ZoneComponent(ZoneId, ZoneType),
            new PositionComponent(position),
            new SpawnPositionComponent(position),
            new HealthComponent(monsterData.Hp),
            new AttackComponent(monsterData.AttackPower, monsterData.AttackRange, monsterData.AttackCooldown),
            new DefenseComponent(monsterData.Defense),
            new AIComponent(monsterData.AggroRange, monsterData.AttackRange, monsterData.MoveSpeed),
            new CombatStateComponent(false, 0)
        );

        AoiGrid.Add(entityId, position.X, position.Z);
        return entity;
    }

    // ── 부활 처리 ────────────────────────────────────────────────────────────

    public void RequestRespawn(long playerId)
        => EnqueueAction(() => HandleRespawn(playerId));

    private void HandleRespawn(long playerId)
    {
        if (!_deadPlayerIds.Contains(playerId)) return;

        World.Query(in _respawnQuery, (Entity entity, ref PlayerComponent player,
            ref EntityIdComponent eid, ref HealthComponent health,
            ref PositionComponent position, ref SessionComponent session) =>
        {
            if (player.PlayerId != playerId) return;

            position.Position = new Vector3(0f, 0f, 0f);
            health.Current    = health.Max;
            _deadPlayerIds.Remove(playerId);

            AoiGrid.Update(eid.EntityId, 0f, 0f);

            session.Session.Send(PacketId.S2C_RespawnResult, new S2C_RespawnResult
            {
                Success   = true,
                Position  = new Vec3 { X = 0f, Y = 0f, Z = 0f },
                CurrentHp = health.Current,
                MaxHp     = health.Max
            });

            Log.Information("Player {PlayerId} respawned at entrance in field ZoneId={ZoneId}", playerId, ZoneId);
        });
    }

    // ── 플레이어 공격 처리 ───────────────────────────────────────────────────

    public void HandleAttack(long attackerEntityId, long targetEntityId, float currentTime)
    {
        Entity? attackerEntity = null;
        Entity? targetEntity   = null;

        World.Query(in _attackQuery, (Entity entity, ref EntityIdComponent eid) =>
        {
            if (eid.EntityId == attackerEntityId) attackerEntity = entity;
            else if (eid.EntityId == targetEntityId) targetEntity = entity;
        });

        if (!attackerEntity.HasValue || !targetEntity.HasValue) return;

        ref var attack = ref attackerEntity.Value.TryGetRef<AttackComponent>(out _);
        if (!attack.CanAttack(currentTime)) return;

        ref var attackerPos = ref attackerEntity.Value.TryGetRef<PositionComponent>(out _);
        ref var targetPos   = ref targetEntity.Value.TryGetRef<PositionComponent>(out _);
        if (attackerPos.Position.Distance(targetPos.Position) > attack.Range) return;

        ref var targetHealth = ref targetEntity.Value.TryGetRef<HealthComponent>(out _);
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
        var deadEntityId   = deadEntity.Get<EntityIdComponent>().EntityId;
        var killerEntityId = killerEntity.Get<EntityIdComponent>().EntityId;

        if (deadEntity.Has<MonsterComponent>() && killerEntity.Has<PlayerComponent>())
            GiveReward(deadEntity, killerEntity);

        BroadcastDeath(deadEntityId, killerEntityId);

        if (deadEntity.Has<PlayerComponent>())
        {
            var playerId = deadEntity.Get<PlayerComponent>().PlayerId;
            _deadPlayerIds.Add(playerId);
            return;
        }

        if (deadEntity.Has<MonsterComponent>())
        {
            var monsterId  = deadEntity.Get<MonsterComponent>().MonsterId;
            var spawnPos   = deadEntity.Has<SpawnPositionComponent>()
                ? deadEntity.Get<SpawnPositionComponent>().Position
                : deadEntity.Get<PositionComponent>().Position;

            AoiGrid.Remove(deadEntityId);
            SubscriberMap.RemoveEntity(deadEntityId);
            World.Destroy(deadEntity);

            _respawnQueue.Add((monsterId, spawnPos, _fieldData.RespawnDelaySeconds));
        }
    }

    private void GiveReward(Entity deadEntity, Entity killerEntity)
    {
        if (!killerEntity.Has<SessionComponent>()) return;

        var monster = deadEntity.Get<MonsterComponent>();
        ref var player  = ref killerEntity.TryGetRef<PlayerComponent>(out _);
        ref var session = ref killerEntity.TryGetRef<SessionComponent>(out _);

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
                ref var health = ref killerEntity.TryGetRef<HealthComponent>(out _);
                ref var atk    = ref killerEntity.TryGetRef<AttackComponent>(out _);
                ref var def    = ref killerEntity.TryGetRef<DefenseComponent>(out _);

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
        ProcessWorldObjects(deltaTime);
        ProcessQuotaTracking(deltaTime);
    }

    private void ProcessWorldObjects(float deltaTime)
    {
        foreach (var obj in _worldObjects)
        {
            bool respawned = obj.Tick(deltaTime);
            if (respawned) BroadcastObjectState(obj);
        }
    }

    // ── WorldObject 채집 ──────────────────────────────────────────────────────

    public void RequestInteract(ISession session, long playerId, int targetObjectId)
        => EnqueueAction(() => HandleInteract(session, playerId, targetObjectId));

    private void HandleInteract(ISession session, long playerId, int targetObjectId)
    {
        var obj = _worldObjects.Find(o => o.ObjectId == targetObjectId);
        if (obj == null)
        {
            session.Send(PacketId.S2C_InteractResult, new S2C_InteractResult
                { ObjectId = targetObjectId, Success = false, Message = "Object not found" });
            return;
        }

        if (!obj.TryHarvest())
        {
            session.Send(PacketId.S2C_InteractResult, new S2C_InteractResult
                { ObjectId = targetObjectId, Success = false, Message = "Object not available" });
            return;
        }

        session.Send(PacketId.S2C_InteractResult, new S2C_InteractResult
        {
            ObjectId = targetObjectId,
            Success  = true,
            Reward   = new ObjectReward { ItemId = obj.ItemId, ItemCount = obj.ItemCount, ExpReward = obj.ExpReward }
        });

        BroadcastObjectState(obj);
        Log.Information("Player {PlayerId} harvested object {ObjectId} (FieldId={FieldId})", playerId, targetObjectId, FieldId);
    }

    public List<S2C_ObjectInfo> GetAllObjectInfos()
    {
        var result = new List<S2C_ObjectInfo>(_worldObjects.Count);
        foreach (var obj in _worldObjects)
        {
            result.Add(new S2C_ObjectInfo
            {
                ObjectId                = obj.ObjectId,
                DataId                  = obj.DataId,
                Position                = new Vec3 { X = obj.Position.X, Y = obj.Position.Y, Z = obj.Position.Z },
                State                   = (int)obj.State,
                RespawnRemainingSeconds = obj.RespawnRemainingSeconds
            });
        }
        return result;
    }

    private void BroadcastObjectState(WorldObject obj)
    {
        BroadcastToAllPlayers(PacketId.S2C_ObjectState, new S2C_ObjectState
        {
            ObjectId                = obj.ObjectId,
            State                   = (int)obj.State,
            RespawnRemainingSeconds = obj.RespawnRemainingSeconds
        });
    }

    private void ProcessRespawnQueue(float deltaTime)
    {
        for (int i = _respawnQueue.Count - 1; i >= 0; i--)
        {
            var (monsterId, pos, delay) = _respawnQueue[i];
            float newDelay = delay - deltaTime;
            if (newDelay <= 0f) { _respawnQueue.RemoveAt(i); SpawnMonster(monsterId, pos); }
            else                { _respawnQueue[i] = (monsterId, pos, newDelay); }
        }
    }

    private void ProcessQuotaTracking(float deltaTime)
    {
        if (_quotaCache.Count == 0) return;

        var playersToEvict = new List<(long playerId, ISession session)>();

        World.Query(in _playerSessQuery, (ref PlayerComponent player, ref SessionComponent session) =>
        {
            if (!_quotaCache.TryGetValue(player.PlayerId, out var state)) return;

            state.ApplyResets();
            state.DailyUsedSeconds  += (int)deltaTime;
            state.WeeklyUsedSeconds += (int)deltaTime;

            int dailyLimit  = _fieldData.DailyLimitMinutes  * 60;
            int weeklyLimit = _fieldData.WeeklyLimitMinutes * 60;

            if (state.DailyUsedSeconds >= dailyLimit || state.WeeklyUsedSeconds >= weeklyLimit)
                playersToEvict.Add((player.PlayerId, session.Session));
        });

        foreach (var (playerId, sess) in playersToEvict)
        {
            sess.Send(PacketId.S2C_FieldQuotaUpdate, new S2C_FieldQuotaUpdate
                { DailyRemainingSeconds = 0, WeeklyRemainingSeconds = 0 });
            sess.Send(PacketId.S2C_LeaveField, new S2C_LeaveField { Success = true });
            _ = RemovePlayerAsync(playerId);
        }

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
        int dailyLimit  = _fieldData.DailyLimitMinutes  * 60;
        int weeklyLimit = _fieldData.WeeklyLimitMinutes * 60;

        World.Query(in _playerSessQuery, (ref PlayerComponent player, ref SessionComponent session) =>
        {
            if (!_quotaCache.TryGetValue(player.PlayerId, out var state)) return;
            session.Session.Send(PacketId.S2C_FieldQuotaUpdate, new S2C_FieldQuotaUpdate
            {
                DailyRemainingSeconds  = Math.Max(0, dailyLimit  - state.DailyUsedSeconds),
                WeeklyRemainingSeconds = Math.Max(0, weeklyLimit - state.WeeklyUsedSeconds)
            });
        });
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

        World.Query(in _allQuery, (Entity entity, ref EntityIdComponent eid) =>
        {
            if (!entity.Has<SessionComponent>()) return;
            if (entity.Get<SessionComponent>().Session != excludeSession) return;
            ref var p = ref entity.TryGetRef<PositionComponent>(out _);
            playerX = p.Position.X; playerZ = p.Position.Z;
        });

        var result = new List<EntityInfo>();

        World.Query(in _allQuery, (Entity entity, ref EntityIdComponent entityId,
            ref PositionComponent pos, ref HealthComponent health) =>
        {
            if (entity.Has<SessionComponent>() && entity.Get<SessionComponent>().Session == excludeSession) return;

            float dx = pos.Position.X - playerX, dz = pos.Position.Z - playerZ;
            if (dx * dx + dz * dz > ViewRadiusSq) return;

            string entityName;
            GameShared.Proto.EntityType entityType;

            if (entity.Has<PlayerComponent>())
            {
                entityName = entity.Get<PlayerComponent>().Name;
                entityType = GameShared.Proto.EntityType.Player;
            }
            else if (entity.Has<MonsterComponent>())
            {
                entityName = entity.Get<MonsterComponent>().Data.Name;
                entityType = GameShared.Proto.EntityType.Monster;
            }
            else return;

            result.Add(new EntityInfo
            {
                EntityId   = entityId.EntityId,
                EntityType = entityType,
                Name       = entityName,
                Position   = new Vec3 { X = pos.Position.X, Y = pos.Position.Y, Z = pos.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            });
        });

        return result;
    }

    // ── 브로드캐스트 헬퍼 ────────────────────────────────────────────────────

    private void BroadcastAttack(long attackerEntityId, long targetEntityId)
        => BroadcastToAllPlayers(PacketId.S2C_Attack,
            new S2C_Attack { AttackerEntityId = attackerEntityId, TargetEntityId = targetEntityId });

    private void BroadcastDamage(long targetEntityId, int damage, int currentHp, int maxHp)
        => BroadcastToAllPlayers(PacketId.S2C_Damage,
            new S2C_Damage { TargetEntityId = targetEntityId, Damage = damage, CurrentHp = currentHp, MaxHp = maxHp });

    private void BroadcastDeath(long deadEntityId, long killerEntityId)
    {
        var packet = new S2C_Death { EntityId = deadEntityId, KillerEntityId = killerEntityId };
        World.Query(in _deathBcastQuery, (Entity entity, ref SessionComponent session,
            ref InterestComponent interest, ref EntityIdComponent entityId) =>
        {
            bool isSelf    = entityId.EntityId == deadEntityId;
            bool isVisible = interest.VisibleEntityIds.Contains(deadEntityId);
            if (!isSelf && !isVisible) return;
            if (session.Session.IsConnected)
                session.Session.Send(PacketId.S2C_Death, packet);
        });
    }

    private void BroadcastToAllPlayers(PacketId packetId, IMessage packet)
    {
        World.Query(in _sessionQuery, (ref SessionComponent session) =>
        {
            if (session.Session.IsConnected)
                session.Session.Send(packetId, packet);
        });
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    private static DateTime GetWeekStart(DateTime dt)
    {
        int diff = (7 + (int)dt.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return dt.Date.AddDays(-diff);
    }
}
