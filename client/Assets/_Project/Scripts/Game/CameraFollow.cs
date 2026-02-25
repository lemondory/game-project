using UnityEngine;

/// <summary>
/// 카메라가 플레이어를 일정 오프셋으로 따라가는 아이소메트릭 뷰
/// TownManager가 Start() 시 target을 설정한다
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float height = 18f;       // 카메라 높이
    public float tiltAngle = 80f;    // 80° ≈ 거의 탑뷰 (90이면 정확히 탑뷰지만 gimbal lock 발생)
    public float smoothSpeed = 8f;

    void LateUpdate()
    {
        if (target == null) return;

        // 카메라를 플레이어 바로 위에 배치
        var desired = target.position + new Vector3(0f, height, 0f);
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);

        // 고정된 탑뷰 회전 (LookAt 대신 Euler 사용 → W=앞, S=뒤 방향 일치)
        transform.rotation = Quaternion.Euler(tiltAngle, 0f, 0f);
    }
}
