using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using GameShared.Enums;

/// <summary>
/// 내 플레이어의 입력 처리 (New Input System)
/// WASD 이동 + 마우스 좌클릭 공격 (던전에서만 활성화)
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("이동")]
    public float moveSpeed = 5f;

    [Header("서버 동기화 간격 (초)")]
    public float sendInterval = 0.1f;

    [Header("공격")]
    public float attackRange = 3f;
    public float attackCooldown = 1f;

    private float _nextSendTime;
    private bool _wasMoving;
    private float _lastAttackTime;

    void Update()
    {
        // 사망 시 입력 차단
        if (DungeonManager.Instance != null && DungeonManager.Instance.IsDead) return;
        if (FieldManager.Instance   != null && FieldManager.Instance.IsDead)   return;

        // UI 입력 필드에 포커스 중이면 입력 무시
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            return;

        HandleMovement();
        HandleAttack();
    }

    // ── 이동 ──────────────────────────────────────────────────────────────────

    private void HandleMovement()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float vertical   = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

        var direction = new Vector3(horizontal, 0f, vertical).normalized;
        bool isMoving = direction != Vector3.zero;

        if (isMoving)
        {
            transform.position += direction * moveSpeed * Time.deltaTime;
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction),
                Time.deltaTime * 15f
            );

            if (Time.time >= _nextSendTime)
            {
                SendMove(transform.position + direction * 0.5f);
                _nextSendTime = Time.time + sendInterval;
            }
        }
        else if (_wasMoving)
        {
            SendMove(transform.position);
        }

        _wasMoving = isMoving;
    }

    // ── 공격 ──────────────────────────────────────────────────────────────────

    private void HandleAttack()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (Time.time - _lastAttackTime < attackCooldown) return;

        // UI 위의 클릭은 무시
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // 현재 씬에서 활성화된 매니저로 타겟 탐색
        long targetEntityId = -1;
        if (DungeonManager.Instance != null)
        {
            targetEntityId = DungeonManager.Instance.FindNearestMonster(transform.position, attackRange);
            if (targetEntityId >= 0) DungeonManager.Instance.SetTarget(targetEntityId);
        }
        else if (FieldManager.Instance != null)
        {
            targetEntityId = FieldManager.Instance.FindNearestMonster(transform.position, attackRange);
            if (targetEntityId >= 0) FieldManager.Instance.SetTarget(targetEntityId);
        }

        if (targetEntityId < 0) return;

        // 공격 대상을 향해 회전
        GameObject targetObj = DungeonManager.Instance != null
            ? DungeonManager.Instance.GetEntityObject(targetEntityId)
            : FieldManager.Instance?.GetEntityObject(targetEntityId);

        if (targetObj != null)
        {
            var lookDir = targetObj.transform.position - transform.position;
            lookDir.y = 0f;
            if (lookDir != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(lookDir);
        }

        _lastAttackTime = Time.time;
        NetworkManager.Instance.Send(PacketId.C2S_Attack, new GameShared.Proto.C2S_Attack
        {
            TargetEntityId = targetEntityId
        });
    }

    // ── 서버 동기화 ───────────────────────────────────────────────────────────

    private void SendMove(Vector3 destination)
    {
        NetworkManager.Instance.Send(PacketId.C2S_Move, new GameShared.Proto.C2S_Move
        {
            Destination = new GameShared.Proto.Vec3
            {
                X = destination.x,
                Y = destination.y,
                Z = destination.z
            }
        });
    }
}
