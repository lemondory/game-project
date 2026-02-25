using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using GameShared.Enums;

/// <summary>
/// 내 플레이어의 입력 처리 및 이동 (New Input System)
/// WASD로 이동하고 주기적으로 서버에 C2S_Move를 전송한다
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("이동")]
    public float moveSpeed = 5f;

    [Header("서버 동기화 간격 (초)")]
    public float sendInterval = 0.1f;

    private float _nextSendTime;
    private bool _wasMoving;

    void Update()
    {
        // UI 입력 필드에 포커스 중이면 이동 입력 무시
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float h = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float v = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

        var dir = new Vector3(h, 0f, v).normalized;
        bool isMoving = dir != Vector3.zero;

        if (isMoving)
        {
            // 이동
            transform.position += dir * moveSpeed * Time.deltaTime;

            // 이동 방향으로 회전
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 15f
            );

            // 주기적으로 서버에 목적지 전송
            if (Time.time >= _nextSendTime)
            {
                SendMove(transform.position + dir * 0.5f);
                _nextSendTime = Time.time + sendInterval;
            }
        }
        else if (_wasMoving)
        {
            // 멈춘 순간 현재 위치를 목적지로 전송
            SendMove(transform.position);
        }

        _wasMoving = isMoving;
    }

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
