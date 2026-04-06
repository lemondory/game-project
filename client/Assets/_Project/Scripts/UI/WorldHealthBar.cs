using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 몬스터/엔티티 머리 위에 표시되는 World Space HP바.
/// 프리팹 구조: Monster → HealthBarCanvas (World Space) → Background → Fill
/// 항상 카메라를 바라보도록 빌보드 처리한다.
/// </summary>
public class WorldHealthBar : MonoBehaviour
{
    [Header("HP바 Fill 이미지")]
    public Image fillImage;

    private Camera _mainCamera;
    private int _currentHp;
    private int _maxHp;

    void Start()
    {
        _mainCamera = Camera.main;

        // 초기에는 숨김 (피격 전까지 HP바 불필요)
        gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        // 카메라를 향해 빌보드 회전
        if (_mainCamera != null)
            transform.rotation = _mainCamera.transform.rotation;
    }

    public void SetHealth(int currentHp, int maxHp)
    {
        _currentHp = currentHp;
        _maxHp = maxHp;

        if (fillImage != null)
            fillImage.fillAmount = maxHp > 0 ? (float)currentHp / maxHp : 0f;

        // 데미지를 받으면 HP바 표시
        if (!gameObject.activeSelf && currentHp < maxHp)
            gameObject.SetActive(true);

        // 죽으면 숨김
        if (currentHp <= 0)
            gameObject.SetActive(false);
    }
}
