using TMPro;
using UnityEngine;

/// <summary>
/// 엔티티 머리 위에 이름을 표시하는 빌보드 레이블
/// 프리팹의 자식 Canvas > Text 오브젝트에 붙인다
/// </summary>
public class EntityLabel : MonoBehaviour
{
    public TMP_Text nameText;

    private Transform _cam;

    void Start()
    {
        _cam = Camera.main?.transform;
    }

    void LateUpdate()
    {
        if (_cam == null) return;
        // 카메라를 향해 회전 (빌보드 효과)
        transform.LookAt(transform.position + _cam.forward);
    }

    public void SetName(string entityName)
    {
        if (nameText != null)
            nameText.text = entityName;
    }
}
