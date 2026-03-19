using UnityEngine;
using GameShared.Proto;
using GameShared.Data;
using GameShared.Generated.Enums;
using GameShared.Generated.Data;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Town 씬의 엔티티(플레이어/몬스터)를 관리하는 싱글톤
/// NetworkManager.PendingEnterTownResult 에서 초기 데이터를 읽어 초기화한다
/// </summary>
public class TownManager : MonoBehaviour
{
    public static TownManager Instance { get; private set; }

    [Header("프리팹")]
    public GameObject myPlayerPrefab;      // 내 플레이어 (PlayerController 포함)
    public GameObject otherPlayerPrefab;   // 다른 플레이어/NPC (EntityMover 포함)
    public GameObject dungeonPortalPrefab; // 던전 포탈 오브젝트

    [Header("카메라")]
    public CameraFollow cameraFollow;

    // 엔티티 추적
    private readonly Dictionary<long, GameObject> _entities = new();

    // 내 플레이어 EntityId
    public long MyEntityId { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        var data = NetworkManager.Instance.PendingEnterTownResult;
        if (data == null)
        {
            Debug.LogError("TownManager: No PendingEnterTownResult! Did you skip login?");
            return;
        }

        Initialize(data);

        // 이후 패킷 이벤트 구독
        NetworkManager.Instance.OnSpawnReceived   += OnSpawn;
        NetworkManager.Instance.OnDespawnReceived += OnDespawn;
        NetworkManager.Instance.OnMoveReceived    += OnMove;
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnSpawnReceived   -= OnSpawn;
        NetworkManager.Instance.OnDespawnReceived -= OnDespawn;
        NetworkManager.Instance.OnMoveReceived    -= OnMove;
    }

    // ── 초기화 ──────────────────────────────────────────────────────────────

    private void Initialize(S2C_EnterTownResult data)
    {
        MyEntityId = data.EntityId;
        var spawnPos = ToUnity(data.Position);

        // 내 플레이어 생성
        var myObj = Instantiate(myPlayerPrefab, spawnPos, Quaternion.identity);
        myObj.name = "MyPlayer";
        _entities[MyEntityId] = myObj;

        // 카메라가 내 플레이어를 따라가도록 설정
        if (cameraFollow != null)
            cameraFollow.target = myObj.transform;

        // 주변 엔티티 스폰
        foreach (var entity in data.NearbyEntities)
            SpawnEntity(entity);

        Debug.Log($"TownManager: Initialized. My EntityId={MyEntityId}, Pos={spawnPos}");

        // MapObjectData에서 현재 존(ZoneId=1)의 오브젝트 스폰
        SpawnMapObjects(zoneId: 1);
    }

    private void SpawnMapObjects(int zoneId)
    {
        if (!GameDataManager.Instance.IsLoaded)
        {
            Debug.LogWarning("TownManager: GameDataManager not loaded, skipping map objects");
            return;
        }

        foreach (var obj in GameDataManager.MapObjectData.Where(o => o.ZoneId == zoneId))
        {
            var pos = new Vector3(obj.PosX, obj.PosY, obj.PosZ);

            switch (obj.ObjectType)
            {
                case ObjectType.DungeonPortal:
                    SpawnDungeonPortal(obj, pos);
                    break;
                // 추후: case ObjectType.Npc: / Shop: / QuestBoard: 등 확장
            }
        }
    }

    private void SpawnDungeonPortal(MapObjectData obj, Vector3 pos)
    {
        if (dungeonPortalPrefab == null)
        {
            Debug.LogWarning("TownManager: dungeonPortalPrefab not assigned in Inspector");
            return;
        }

        var dungeonData = GameDataManager.DungeonData.GetById(obj.ReferenceId);
        if (dungeonData == null)
        {
            Debug.LogWarning($"TownManager: DungeonId={obj.ReferenceId} not found in DungeonData");
            return;
        }

        var go = Instantiate(dungeonPortalPrefab, pos, Quaternion.identity);
        go.name = $"Portal_{dungeonData.Name}";

        var portal = go.GetComponent<DungeonPortal>();
        if (portal != null)
        {
            portal.DungeonId   = dungeonData.DungeonId;
            portal.DungeonName = dungeonData.Name;
        }

        Debug.Log($"TownManager: Spawned portal '{dungeonData.Name}' at {pos}");
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
        // 내 플레이어는 PlayerController가 직접 움직이므로 무시
        if (packet.EntityId == MyEntityId) return;

        if (_entities.TryGetValue(packet.EntityId, out var obj))
        {
            var mover = obj.GetComponent<EntityMover>();
            if (mover != null)
                // BroadcastSystem은 현재 위치(Position)만 채움 → Position 사용
                mover.SetDestination(ToUnity(packet.Position));
        }
    }

    // ── 내부 헬퍼 ───────────────────────────────────────────────────────────

    private void SpawnEntity(EntityInfo info)
    {
        if (_entities.ContainsKey(info.EntityId)) return; // 이미 존재

        var pos = ToUnity(info.Position);
        var prefab = info.EntityId == MyEntityId ? myPlayerPrefab : otherPlayerPrefab;
        var obj = Instantiate(prefab, pos, Quaternion.identity);
        obj.name = $"{info.Name}_{info.EntityId}";

        // 이름 레이블 설정 (EntityLabel 컴포넌트가 있으면)
        var label = obj.GetComponent<EntityLabel>();
        if (label != null)
            label.SetName(info.Name);

        _entities[info.EntityId] = obj;
    }

    private static Vector3 ToUnity(GameShared.Proto.Vec3 v)
    {
        if (v == null) return Vector3.zero;
        return new Vector3(v.X, v.Y, v.Z);
    }
}
