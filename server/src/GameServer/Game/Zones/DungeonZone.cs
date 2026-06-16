using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
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

    public int        DungeonId    { get; }
    public List<long> PartyMembers { get; } = new();
    public DateTime   CreatedTime  { get; }

    private readonly DungeonData _dungeonData;
    private readonly Random      _random = new();

    private bool  _finished    = false;
    private float _elapsedTime = 0f;
    private int   _killCount   = 0;

    private const float ExitDelay = 10f;
    private float _exitTimer = 0f;

    private const float DeathReturnDelay = 3f;
    private readonly List<(ISession session, long playerId, float remaining)> _deadPlayers = new();

    private int _remainingMonsters = 0;
    private readonly List<(int monsterId, Vector3 position, float delay)> _respawnQueue = new();

    private const float TimerBroadcastInterval  = 1f;
    private float       _timerBroadcastCooldown = 0f;

    // 공통 쿼리
    private readonly QueryDescription _allQuery        = new QueryDescription().WithAll<EntityIdComponent>();
    private readonly QueryDescription _playerQuery     = new QueryDescription().WithAll<PlayerComponent, EntityIdComponent>();
    private readonly QueryDescription _sessionQuery    = new QueryDescription().WithAll<SessionComponent>();
    private readonly QueryDescription _deathBcastQuery = new QueryDescription().WithAll<SessionComponent, InterestComponent, EntityIdComponent>();
    private readonly QueryDescription _attackQuery     = new QueryDescription().WithAll<EntityIdComponent, AttackComponent, PositionComponent>();

    public DungeonZone(int zoneId, int dungeonId) : base(zoneId, ZoneType.Dungeon)
    {
        DungeonId   = dungeonId;
        CreatedTime = DateTime.UtcNow;
        _dungeonData = GameDataManager.DungeonData.GetById(dungeonId)
            ?? throw new ArgumentException($"Unknown dungeonId={dungeonId}");
    }

    protected override ISystem<float> CreateSystems()
    {
        return new Group<float>($"Dungeon-{ZoneId}",
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
        PartyMembers.Add(playerId);

        Log.Information("Player entered dungeon: {PlayerId} - {PlayerName} (EntityId={EntityId}, DungeonId={DungeonId})",
            playerId, playerName, entityId, DungeonId);

        return entity;
    }

    public void RemovePlayer(long playerId)
    {
        PartyMembers.Remove(playerId);

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

        Log.Information("Player {PlayerId} left dungeon ZoneId={ZoneId}", playerId, ZoneId);
    }

    // ── 몬스터 스폰 ──────────────────────────────────────────────────────────

    public Entity SpawnMonster(int monsterId, Vector3 position)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);

        var monsterData = GameDataManager.MonsterData.GetById(monsterId);
        if (monsterData == null)
        {
            Log.Warning("SpawnMonster: unknown monsterId={MonsterId}", monsterId);
            return default;
        }

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

        if (_dungeonData.DungeonType == DungeonType.KillAll)
            _remainingMonsters++;

        Log.Information("Monster spawned: {Name}(Id={MonsterId}) EntityId={EntityId}",
            monsterData.Name, monsterId, entityId);

        return entity;
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

    // ── 사망 처리 ────────────────────────────────────────────────────────────

    private void HandleDeath(Entity deadEntity, Entity killerEntity)
    {
        var deadEntityId   = deadEntity.Get<EntityIdComponent>().EntityId;
        var killerEntityId = killerEntity.Get<EntityIdComponent>().EntityId;

        Log.Information("Entity {DeadId} killed by {KillerId}", deadEntityId, killerEntityId);

        if (deadEntity.Has<MonsterComponent>() && killerEntity.Has<PlayerComponent>())
        {
            GiveReward(deadEntity, killerEntity);
            _killCount++;
        }

        BroadcastDeath(deadEntityId, killerEntityId);

        if (deadEntity.Has<PlayerComponent>())
        {
            var session  = deadEntity.Get<SessionComponent>().Session;
            var playerId = deadEntity.Get<PlayerComponent>().PlayerId;

            AoiGrid.Remove(deadEntityId);
            SubscriberMap.RemoveEntity(deadEntityId);
            World.Destroy(deadEntity);

            _deadPlayers.Add((session, playerId, DeathReturnDelay));
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

            if (_finished) return;

            switch (_dungeonData.DungeonType)
            {
                case DungeonType.KillAll:
                    _remainingMonsters = Math.Max(0, _remainingMonsters - 1);
                    if (_remainingMonsters == 0) TriggerFinish(isCleared: true);
                    break;
                case DungeonType.Timed:
                    if (_dungeonData.RespawnDelaySeconds > 0)
                        _respawnQueue.Add((monsterId, spawnPos, _dungeonData.RespawnDelaySeconds));
                    break;
            }
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

    // ── 클리어/실패 판정 ─────────────────────────────────────────────────────

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

    // ── 게임 루프 훅 ────────────────────────────────────────────────────────

    protected override void OnUpdate(float deltaTime)
    {
        ProcessDeadPlayers(deltaTime);

        if (_finished)
        {
            _exitTimer += deltaTime;
            if (_exitTimer >= ExitDelay) EvictAllAndDestroy();
            return;
        }

        _elapsedTime += deltaTime;

        if (_dungeonData.TimeLimitSeconds > 0)
        {
            int remaining = Math.Max(0, _dungeonData.TimeLimitSeconds - (int)_elapsedTime);

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
                bool isCleared = _dungeonData.DungeonType == DungeonType.Timed;
                TriggerFinish(isCleared);
                return;
            }
        }

        if (_dungeonData.DungeonType == DungeonType.Timed)
            ProcessRespawnQueue(deltaTime);
    }

    private void ProcessDeadPlayers(float deltaTime)
    {
        for (int i = _deadPlayers.Count - 1; i >= 0; i--)
        {
            var (session, playerId, remaining) = _deadPlayers[i];
            float newRemaining = remaining - deltaTime;
            if (newRemaining <= 0f)
            {
                _deadPlayers.RemoveAt(i);
                session.Send(PacketId.S2C_LeaveDungeon, new S2C_LeaveDungeon { Success = true });
                RemovePlayer(playerId);
            }
            else
            {
                _deadPlayers[i] = (session, playerId, newRemaining);
            }
        }
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

    // ── 퇴장 ────────────────────────────────────────────────────────────────

    private void EvictAllAndDestroy()
    {
        var sessions = new List<ISession>();
        World.Query(in _sessionQuery, (ref SessionComponent session) =>
        {
            if (session.Session.IsConnected)
                sessions.Add(session.Session);
        });

        foreach (var session in sessions)
            session.Send(PacketId.S2C_LeaveDungeon, new S2C_LeaveDungeon { Success = true });

        foreach (var (session, _, _) in _deadPlayers)
            if (session.IsConnected)
                session.Send(PacketId.S2C_LeaveDungeon, new S2C_LeaveDungeon { Success = true });
        _deadPlayers.Clear();

        Log.Information("Dungeon evicted all players: ZoneId={ZoneId}", ZoneId);
        Task.Run(() => ZoneManager.Instance.RemoveZone(ZoneId));
    }

    // ── 입장 시 주변 엔티티 정보 ─────────────────────────────────────────────

    public List<EntityInfo> GetNearbyEntityInfos(ISession excludeSession)
    {
        float playerX = 0f, playerZ = 0f;
        var fullQuery = new QueryDescription().WithAll<EntityIdComponent, PositionComponent, HealthComponent>();

        World.Query(in fullQuery, (Entity entity, ref EntityIdComponent eid) =>
        {
            if (!entity.Has<SessionComponent>()) return;
            if (entity.Get<SessionComponent>().Session != excludeSession) return;
            ref var p = ref entity.TryGetRef<PositionComponent>(out _);
            playerX = p.Position.X;
            playerZ = p.Position.Z;
        });

        var result = new List<EntityInfo>();

        World.Query(in fullQuery, (Entity entity, ref EntityIdComponent entityId,
            ref PositionComponent pos, ref HealthComponent health) =>
        {
            if (entity.Has<SessionComponent>() && entity.Get<SessionComponent>().Session == excludeSession) return;

            float distX = pos.Position.X - playerX;
            float distZ = pos.Position.Z - playerZ;
            if (distX * distX + distZ * distZ > ViewRadiusSq) return;

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
}
