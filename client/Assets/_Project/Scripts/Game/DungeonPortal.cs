using UnityEngine;
using UnityEngine.InputSystem;
using GameShared.Generated.Data;

/// <summary>
/// 던전 입장 포탈 오브젝트.
/// Rigidbody/Collider 없이 매 프레임 플레이어와의 거리로 범위 감지.
/// </summary>
public class DungeonPortal : MonoBehaviour
{
    [Header("데이터")]
    public int DungeonId;
    public string DungeonName;

    [Header("설정")]
    public float interactRadius = 3f;

    private bool _playerInRange;
    private Transform _playerTransform;

    void Start()
    {
        // 플레이어 트랜스폼 캐싱 (MyPlayer 태그 사용)
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _playerTransform = playerObj.transform;
    }

    void Update()
    {
        // 플레이어가 늦게 스폰될 경우 재탐색
        if (_playerTransform == null)
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                _playerTransform = playerObj.transform;
            return;
        }

        // XZ 평면 거리만 비교 (Y 높이 차이 무시)
        var selfXZ   = new Vector2(transform.position.x, transform.position.z);
        var playerXZ = new Vector2(_playerTransform.position.x, _playerTransform.position.z);
        bool inRange = Vector2.Distance(selfXZ, playerXZ) < interactRadius;

        if (inRange != _playerInRange)
        {
            _playerInRange = inRange;
            DungeonEntryPopup.Instance?.ShowPrompt(inRange, inRange ? DungeonName : string.Empty);
        }

        if (_playerInRange && Keyboard.current.eKey.wasPressedThisFrame)
            OpenPopup();
    }

    private void OpenPopup()
    {
        var data = GameShared.Data.GameDataManager.DungeonData.GetById(DungeonId);
        DungeonEntryPopup.Instance?.Open(data);
    }
}
