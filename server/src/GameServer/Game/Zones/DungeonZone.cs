using DefaultEcs;
using DefaultEcs.System;
using GameServer.Game.Components;
using GameServer.Game.Systems;
using GameServer.Network;
using GameShared.Data;
using GameShared.Enums;
using GameShared.Generated.Data;
using GameShared.Generated.Enums;
using GameShared.Proto;
using GameShared.Utils;
using Google.Protobuf;
using Serilog;

namespace GameServer.Game.Zones;

/// <summary>
/// 인스턴스 던전 존.
/// DungeonType.KillAll  — 제한 시간 내 모든 몬스터 처치 시 클리어. 시간 초과 시 실패 퇴장.
/// DungeonType.Timed    — 제한 시간 동안 최대한 처치. 시간 종료 시 클리어 처리 후 퇴장.
///                        RespawnDelaySeconds마다 초기 몬스터를 다시 스폰.
/// </summary>
public class DungeonZone : Zone
{
    private const float ViewRadius   = 50f;
    private const float ViewRadiusSq = ViewRadius * ViewRadius;

    private static long _nextEntityId = 10000;

    public int  DungeonId    { get; }
    public List<long> PartyMembers { get; } = new();
    public DateTime   CreatedTime  { get; }

    private readonly DungeonData _dungeonData;
    private readonly Random      _random = new();

    // ── 공통 ────────────────────────────────────────────────────
    private bool  _finished  = false; // 클리어 또는 실패 판정 완료
    private float _elapsedTime = 0f;  // 던전 입장 후 경과 시간
    private int   _killCount = 0;     // 총 처치 몬스터 수

    // 클리어/실패 후 퇴장까지 대기
    private const float ExitDelay = 10f;
    private float _exitTimer = 0f;

    // ── KillAll 전용 ─────────────────────────────────────────────
    private int _remainingMonsters = 0;

    // ── Timed 전용 ───────────────────────────────────────────────
    // 리스폰 대기 중인 (monsterId, position, 남은 대기 시간) 목록
    private readonly List<(int monsterId, Vector3 position, float delay)> _respawnQueue = new();

    // 타이머 업데이트 브로드캐스트 주기
    private const float TimerBroadcastInterval = 1f;
    private float _timerBroadcastCooldown = 0f;

    public DungeonZone(int zoneId, int dungeonId) : base(zoneId, ZoneType.Dungeon)
    {
        DungeonId   = dungeonId;
        CreatedTime = DateTime.UtcNow;
        _dungeonData = GameDataManager.DungeonData.GetById(dungeonId)
            ?? throw new ArgumentException($"Unknown dungeonId={dungeonId}");
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

    // ── 플레이어 관리 ────────────────────────────────────────────

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
        PartyMembers.Add(playerId);

        Log.Information("Player entered dungeon: {PlayerId} - {PlayerName} (EntityId={EntityId}, DungeonId={DungeonId})",
            playerId, playerName, entityId, DungeonId);

        return entity;
    }

    public void RemovePlayer(long playerId)
    {
        PartyMembers.Remove(playerId);

        var playerEntities = World.GetEntities()
            .With<PlayerComponent>()
            .With<EntityIdComponent>()
            .AsSet();

        foreach (var entity in playerEntities.GetEntities())
        {
            if (!entity.IsAlive) continue;
            ref var player = ref entity.Get<PlayerComponent>();
            if (player.PlayerId != playerId) continue;

            ref var eid = ref entity.Get<EntityIdComponent>();
            AoiGrid.Remove(eid.EntityId);
            SubscriberMap.RemoveEntity(eid.EntityId);
            entity.Dispose();
            break;
        }

        Log.Information("Player {PlayerId} left dungeon ZoneId={ZoneId}", playerId, ZoneId);
    }

    // ── 몬스터 스폰 ──────────────────────────────────────────────

    public Entity SpawnMonster(int monsterId, Vector3 position)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        var monsterData = GameDataManager.MonsterData.GetById(monsterId);
        if (monsterData == null)
        {
            Log.Warning("SpawnMonster: unknown monsterId={MonsterId}", monsterId);
            return default;
        }

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

        if (_dungeonData.DungeonType == DungeonType.KillAll)
            _remainingMonsters++;

        Log.Information("Monster spawned: {Name}(Id={MonsterId}) EntityId={EntityId}",
            monsterData.Name, monsterId, entityId);

        return entity;
    }

    // ── 플레이어 공격 처리 ───────────────────────────────────────

    public void HandleAttack(long attackerEntityId, long targetEntityId, float currentTime)
    {
        var entities = World.GetEntities()
            .With<EntityIdComponent>()
            .With<AttackComponent>()
            .AsSet();

        Entity? attackerEntity = null;
        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == attackerEntityId) { attackerEntity = entity; break; }
        }
        if (!attackerEntity.HasValue) return;

        ref var attack = ref attackerEntity.Value.Get<AttackComponent>();
        if (!attack.CanAttack(currentTime)) return;

        Entity? targetEntity = null;
        foreach (var entity in entities.GetEntities())
        {
            ref var id = ref entity.Get<EntityIdComponent>();
            if (id.EntityId == targetEntityId) { targetEntity = entity; break; }
        }
        if (!targetEntity.HasValue) return;

        ref var attackerPos = ref attackerEntity.Value.Get<PositionComponent>();
        ref var targetPos   = ref targetEntity.Value.Get<PositionComponent>();
        if (attackerPos.Position.Distance(targetPos.Position) > attack.Range) return;

        ref var targetHealth = ref targetEntity.Value.Get<HealthComponent>();
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

    // ── 사망 처리 ────────────────────────────────────────────────

    private void HandleDeath(Entity deadEntity, Entity killerEntity)
    {
        ref var deadEntityId   = ref deadEntity.Get<EntityIdComponent>();
        ref var killerEntityId = ref killerEntity.Get<EntityIdComponent>();

        Log.Information("Entity {DeadId} killed by {KillerId}", deadEntityId.EntityId, killerEntityId.EntityId);

        if (deadEntity.Has<MonsterComponent>() && killerEntity.Has<PlayerComponent>())
        {
            GiveReward(deadEntity, killerEntity);
            _killCount++;
        }

        BroadcastDeath(deadEntityId.EntityId, killerEntityId.EntityId);

        if (deadEntity.Has<MonsterComponent>())
        {
            ref var monsterComp = ref deadEntity.Get<MonsterComponent>();
            var deadPos = deadEntity.Get<PositionComponent>().Position;

            AoiGrid.Remove(deadEntityId.EntityId);
            SubscriberMap.RemoveEntity(deadEntityId.EntityId);
            deadEntity.Dispose();

            if (_finished) return;

            switch (_dungeonData.DungeonType)
            {
                case DungeonType.KillAll:
                    _remainingMonsters = Math.Max(0, _remainingMonsters - 1);
                    if (_remainingMonsters == 0)
                        TriggerFinish(isCleared: true);
                    break;

                case DungeonType.Timed:
                    // 리스폰 큐에 등록
                    if (_dungeonData.RespawnDelaySeconds > 0)
                        _respawnQueue.Add((monsterComp.MonsterId, deadPos, _dungeonData.RespawnDelaySeconds));
                    break;
            }
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

    // ── 클리어/실패 판정 ─────────────────────────────────────────

    private void TriggerFinish(bool isCleared)
    {
        _finished  = true;
        _exitTimer = 0f;

        int elapsed = (int)_elapsedTime;

        Log.Information("Dungeon finished: ZoneId={ZoneId}, DungeonId={DungeonId}, Cleared={Cleared}, KillCount={KillCount}, Time={Time}s",
            ZoneId, DungeonId, isCleared, _killCount, elapsed);

        BroadcastToAllPlayers(PacketId.S2C_DungeonClear, new S2C_DungeonClear
        {
            DungeonId        = DungeonId,
            ClearTimeSeconds = elapsed,
            IsCleared        = isCleared,
            KillCount        = _killCount
        });
    }

    // ── 게임 루프 훅 ────────────────────────────────────────────

    protected override void OnUpdate(float deltaTime)
    {
        if (_finished)
        {
            _exitTimer += deltaTime;
            if (_exitTimer >= ExitDelay)
                EvictAllAndDestroy();
            return;
        }

        _elapsedTime += deltaTime;

        // 제한 시간 체크 (KillAll + Timed 공통)
        if (_dungeonData.TimeLimitSeconds > 0)
        {
            int remaining = Math.Max(0, _dungeonData.TimeLimitSeconds - (int)_elapsedTime);

            // 1초마다 타이머 브로드캐스트
            _timerBroadcastCooldown -= deltaTime;
            if (_timerBroadcastCooldown <= 0f)
            {
                _timerBroadcastCooldown = TimerBroadcastInterval;
                BroadcastToAllPlayers(PacketId.S2C_DungeonTimerUpdate, new S2C_DungeonTimerUpdate
                {
                    RemainingSeconds = remaining,
                    KillCount        = _killCount
                });
            }

            if (_elapsedTime >= _dungeonData.TimeLimitSeconds)
            {
                // KillAll: 시간 초과 = 실패 / Timed: 시간 종료 = 클리어
                bool isCleared = _dungeonData.DungeonType == DungeonType.Timed;
                TriggerFinish(isCleared);
                return;
            }
        }

        // Timed 전용: 리스폰 큐 처리
        if (_dungeonData.DungeonType == DungeonType.Timed)
            ProcessRespawnQueue(deltaTime);
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

    // ── 퇴장 ────────────────────────────────────────────────────

    private void EvictAllAndDestroy()
    {
        var sessions = new List<ISession>();
        var playerEntities = World.GetEntities()
            .With<SessionComponent>()
            .With<PlayerComponent>()
            .AsSet();

        foreach (var entity in playerEntities.GetEntities())
        {
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                sessions.Add(session.Session);
        }

        foreach (var session in sessions)
            session.Send(PacketId.S2C_LeaveDungeon, new S2C_LeaveDungeon { Success = true });

        Log.Information("Dungeon evicted all players: ZoneId={ZoneId}", ZoneId);
        Task.Run(() => ZoneManager.Instance.RemoveZone(ZoneId));
    }

    // ── 입장 시 주변 엔티티 정보 ─────────────────────────────────

    public List<EntityInfo> GetNearbyEntityInfos(ISession excludeSession)
    {
        float playerX = 0f, playerZ = 0f;
        var allEntities = World.GetEntities()
            .With<EntityIdComponent>()
            .With<PositionComponent>()
            .With<HealthComponent>()
            .AsSet();

        foreach (var candidate in allEntities.GetEntities())
        {
            if (!candidate.Has<SessionComponent>()) continue;
            if (candidate.Get<SessionComponent>().Session != excludeSession) continue;
            ref var playerPos = ref candidate.Get<PositionComponent>();
            playerX = playerPos.Position.X;
            playerZ = playerPos.Position.Z;
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

            ref var pos = ref entity.Get<PositionComponent>();
            float distX = pos.Position.X - playerX;
            float distZ = pos.Position.Z - playerZ;
            if (distX * distX + distZ * distZ > ViewRadiusSq) continue;

            ref var entityId = ref entity.Get<EntityIdComponent>();
            ref var health   = ref entity.Get<HealthComponent>();

            string entityName;
            GameShared.Proto.EntityType entityType;

            if (entity.Has<PlayerComponent>())
            {
                ref var player = ref entity.Get<PlayerComponent>();
                entityName = player.Name;
                entityType = GameShared.Proto.EntityType.Player;
            }
            else if (entity.Has<MonsterComponent>())
            {
                ref var monster = ref entity.Get<MonsterComponent>();
                entityName = monster.Data.Name;
                entityType = GameShared.Proto.EntityType.Monster;
            }
            else continue;

            result.Add(new EntityInfo
            {
                EntityId   = entityId.EntityId,
                EntityType = entityType,
                Name       = entityName,
                Position   = new Vec3 { X = pos.Position.X, Y = pos.Position.Y, Z = pos.Position.Z },
                CurrentHp  = health.Current,
                MaxHp      = health.Max
            });
        }

        return result;
    }

    // ── 브로드캐스트 헬퍼 ────────────────────────────────────────

    private void BroadcastAttack(long attackerEntityId, long targetEntityId)
    {
        BroadcastToAllPlayers(PacketId.S2C_Attack, new S2C_Attack
        {
            AttackerEntityId = attackerEntityId,
            TargetEntityId   = targetEntityId
        });
    }

    private void BroadcastDamage(long targetEntityId, int damage, int currentHp, int maxHp)
    {
        BroadcastToAllPlayers(PacketId.S2C_Damage, new S2C_Damage
        {
            TargetEntityId = targetEntityId,
            Damage         = damage,
            CurrentHp      = currentHp,
            MaxHp          = maxHp
        });
    }

    private void BroadcastDeath(long deadEntityId, long killerEntityId)
    {
        var packet = new S2C_Death { EntityId = deadEntityId, KillerEntityId = killerEntityId };
        var entities = World.GetEntities()
            .With<SessionComponent>()
            .With<InterestComponent>()
            .With<EntityIdComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            ref var entityId = ref entity.Get<EntityIdComponent>();
            var interest = entity.Get<InterestComponent>();
            bool isSelf    = entityId.EntityId == deadEntityId;
            bool isVisible = interest.VisibleEntityIds.Contains(deadEntityId);
            if (!isSelf && !isVisible) continue;

            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(PacketId.S2C_Death, packet);
        }
    }

    private void BroadcastToAllPlayers(PacketId packetId, IMessage packet)
    {
        var entities = World.GetEntities()
            .With<SessionComponent>()
            .AsSet();

        foreach (var entity in entities.GetEntities())
        {
            ref var session = ref entity.Get<SessionComponent>();
            if (session.Session.IsConnected)
                session.Session.Send(packetId, packet);
        }
    }
}
