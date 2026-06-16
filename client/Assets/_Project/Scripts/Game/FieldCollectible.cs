using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Field 씬의 채집 오브젝트. FieldManager가 S2C_ObjectInfo 수신 시 생성한다.
/// Available 상태일 때만 E키 채집이 가능하고, 채집 후 시각적으로 비활성화된다.
/// </summary>
public class FieldCollectible : MonoBehaviour
{
    public int ObjectId { get; private set; }

    public float interactRadius = 2f;

    private int _state;  // 0=Available, 1=Harvested, 2=Respawning
    private Transform _playerTransform;
    private Renderer _renderer;
    private bool _playerInRange;

    private static readonly Color ColorAvailable   = new Color(0.2f, 0.8f, 0.2f);
    private static readonly Color ColorUnavailable = new Color(0.4f, 0.4f, 0.4f);

    public void Initialize(int objectId, int state)
    {
        ObjectId = objectId;
        _renderer = GetComponent<Renderer>();
        ApplyState(state);
    }

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

        bool available = _state == 0;
        float dx = transform.position.x - _playerTransform.position.x;
        float dz = transform.position.z - _playerTransform.position.z;
        bool inRange = available && (dx * dx + dz * dz) < interactRadius * interactRadius;

        if (inRange != _playerInRange)
        {
            _playerInRange = inRange;
            // TODO: 프롬프트 UI 연결 (FieldEntryPopup 구조 참고)
        }

        if (_playerInRange && Keyboard.current.eKey.wasPressedThisFrame)
            NetworkManager.Instance.SendInteract(ObjectId);
    }

    public void SetState(int state)
    {
        _state = state;
        ApplyState(state);
    }

    private void ApplyState(int state)
    {
        _state = state;
        bool available = state == 0;
        if (_renderer != null)
            _renderer.material.color = available ? ColorAvailable : ColorUnavailable;
    }
}
