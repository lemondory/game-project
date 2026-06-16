using UnityEngine;
using UnityEngine.InputSystem;
using GameShared.Generated.Data;

/// <summary>
/// 시간제 사냥터 입장 포탈.
/// DungeonPortal과 동일한 구조로 범위 감지 + E키 입장.
/// </summary>
public class FieldPortal : MonoBehaviour
{
    [Header("데이터")]
    public int FieldId;
    public string FieldName;

    [Header("설정")]
    public float interactRadius = 3f;

    private bool _playerInRange;
    private Transform _playerTransform;

    void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _playerTransform = playerObj.transform;
    }

    void Update()
    {
        if (_playerTransform == null)
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                _playerTransform = playerObj.transform;
            return;
        }

        var selfXZ   = new Vector2(transform.position.x, transform.position.z);
        var playerXZ = new Vector2(_playerTransform.position.x, _playerTransform.position.z);
        bool inRange = Vector2.Distance(selfXZ, playerXZ) < interactRadius;

        if (inRange != _playerInRange)
        {
            _playerInRange = inRange;
            FieldEntryPopup.Instance?.ShowPrompt(inRange, inRange ? FieldName : string.Empty);
        }

        if (_playerInRange && Keyboard.current.eKey.wasPressedThisFrame)
            OpenPopup();
    }

    private void OpenPopup()
    {
        var data = GameShared.Data.GameDataManager.TimeLimitedFieldData.GetById(FieldId);
        FieldEntryPopup.Instance?.Open(data);
    }
}
