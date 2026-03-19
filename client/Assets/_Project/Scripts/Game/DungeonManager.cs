using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using GameShared.Proto;
using System.Collections.Generic;

/// <summary>
/// Dungeon 씬의 엔티티(플레이어/몬스터)를 관리하는 싱글톤
/// NetworkManager.PendingEnterDungeonResult 에서 초기 데이터를 읽어 초기화한다
/// </summary>
public class DungeonManager : MonoBehaviour
{
    public static DungeonManager Instance { get; private set; }

    [Header("프리팹")]
    public GameObject myPlayerPrefab;
    public GameObject otherPlayerPrefab;
    public GameObject monsterPrefab;

    [Header("카메라")]
    public CameraFollow cameraFollow;

    // 엔티티 추적
    private readonly Dictionary<long, GameObject> _entities = new();

    public long MyEntityId { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        var data = NetworkManager.Instance.PendingEnterDungeonResult;
        if (data == null)
        {
            Debug.LogError("DungeonManager: No PendingEnterDungeonResult!");
            return;
        }

        Initialize(data);

        NetworkManager.Instance.OnSpawnReceived   += OnSpawn;
        NetworkManager.Instance.OnDespawnReceived += OnDespawn;
        NetworkManager.Instance.OnMoveReceived    += OnMove;
        NetworkManager.Instance.OnDamageReceived  += OnDamage;
        NetworkManager.Instance.OnDeathReceived   += OnDeath;
        NetworkManager.Instance.OnRewardReceived  += OnReward;
        NetworkManager.Instance.OnLevelUpReceived += OnLevelUp;
    }

    void Update()
    {
        // ESC → 마을로 복귀
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            LeaveDungeon();
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnSpawnReceived   -= OnSpawn;
        NetworkManager.Instance.OnDespawnReceived -= OnDespawn;
        NetworkManager.Instance.OnMoveReceived    -= OnMove;
        NetworkManager.Instance.OnDamageReceived  -= OnDamage;
        NetworkManager.Instance.OnDeathReceived   -= OnDeath;
        NetworkManager.Instance.OnRewardReceived  -= OnReward;
        NetworkManager.Instance.OnLevelUpReceived -= OnLevelUp;
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    private void Initialize(S2C_EnterDungeonResult data)
    {
        MyEntityId = data.EntityId;
        var spawnPos = ToUnity(data.Position);

        // 내 플레이어 생성
        var prefab = myPlayerPrefab != null ? myPlayerPrefab : otherPlayerPrefab;
        if (prefab != null)
        {
            var myObj = Instantiate(prefab, spawnPos, Quaternion.identity);
            myObj.name = "MyPlayer";
            _entities[MyEntityId] = myObj;

            if (cameraFollow != null)
                cameraFollow.target = myObj.transform;
        }

        // 주변 엔티티 스폰
        foreach (var entity in data.NearbyEntities)
            SpawnEntity(entity);

        Debug.Log($"DungeonManager: Initialized. EntityId={MyEntityId}, entities={data.NearbyEntities.Count}");
    }

    // ── 패킷 핸들러 ─────────────────────────────────────────────────────────

    private void OnSpawn(S2C_Spawn packet)
    {
        if (packet.Entity == null) return;
        SpawnEntity(packet.Entity);
    }

    private void OnDespawn(S2C_Despawn packet)
    {
        if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            Destroy(obj);
            _entities.Remove(packet.EntityId);
        }
    }

    private void OnMove(S2C_Move packet)
    {
        if (packet.EntityId == MyEntityId) return;

        if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            var mover = obj.GetComponent<EntityMover>();
            if (mover != null)
                mover.SetDestination(ToUnity(packet.Position));
        }
    }

    // ── 내부 헬퍼 ───────────────────────────────────────────────────────────

    private void SpawnEntity(EntityInfo info)
    {
        if (_entities.ContainsKey(info.EntityId)) return;

        var pos = ToUnity(info.Position);

        // EntityType으로 몬스터/플레이어 구분
        bool isMonster = info.EntityType == EntityType.Monster;
        GameObject prefab;
        if (isMonster)
            prefab = monsterPrefab != null ? monsterPrefab : otherPlayerPrefab;
        else if (info.EntityId == MyEntityId)
            prefab = myPlayerPrefab;
        else
            prefab = otherPlayerPrefab;

        if (prefab == null) return;

        var obj = Instantiate(prefab, pos, Quaternion.identity);
        obj.name = $"{info.Name}_{info.EntityId}";

        var label = obj.GetComponent<EntityLabel>();
        if (label != null)
            label.SetName(info.Name);

        _entities[info.EntityId] = obj;
    }

    private void OnDamage(GameShared.Proto.S2C_Damage packet)
    {
        if (packet.TargetEntityId == MyEntityId)
        {
            Debug.Log($"[DungeonManager] Player damaged: {packet.Damage} dmg, HP={packet.CurrentHp}/{packet.MaxHp}");
            // TODO: HP UI 업데이트
        }
    }

    private void OnDeath(GameShared.Proto.S2C_Death packet)
    {
        if (packet.EntityId == MyEntityId)
        {
            Debug.Log("[DungeonManager] Player died!");
            // TODO: 사망 처리 UI
        }
        else if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            Destroy(obj);
            _entities.Remove(packet.EntityId);
        }
    }

    private void OnReward(GameShared.Proto.S2C_RewardResult packet)
    {
        Debug.Log($"[DungeonManager] Reward: +{packet.ExpReward} EXP, +{packet.GoldReward} Gold (total exp={packet.TotalExp}, gold={packet.TotalGold})");
        // TODO: 보상 UI 표시
    }

    private void OnLevelUp(GameShared.Proto.S2C_LevelUp packet)
    {
        Debug.Log($"[DungeonManager] Level Up! Lv={packet.NewLevel}, MaxHP={packet.NewMaxHp}, ATK={packet.NewAttack}, DEF={packet.NewDefense}");
        // TODO: 레벨업 UI 표시
    }

    public void LeaveDungeon()
    {
        // 던전 나가기: 마을 재입장 요청
        NetworkManager.Instance.Send(GameShared.Enums.PacketId.C2S_EnterTown,
            new C2S_EnterTown());
        Debug.Log("DungeonManager: Leaving dungeon...");
    }

    private static Vector3 ToUnity(GameShared.Proto.Vec3 v)
    {
        if (v == null) return Vector3.zero;
        return new Vector3(v.X, v.Y, v.Z);
    }
}
