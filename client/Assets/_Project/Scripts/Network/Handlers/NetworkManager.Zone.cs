using UnityEngine;
using UnityEngine.SceneManagement;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<S2C_EnterDungeonResult>  OnEnterDungeonReceived;
    public event Action<S2C_DungeonClear>        OnDungeonClearReceived;
    public event Action                          OnLeaveDungeonReceived;
    public event Action<S2C_DungeonTimerUpdate>  OnDungeonTimerUpdateReceived;

    // 시간제 사냥터
    public event Action<S2C_EnterFieldResult>   OnEnterFieldReceived;
    public event Action                         OnLeaveFieldReceived;
    public event Action<S2C_FieldQuotaUpdate>   OnFieldQuotaUpdateReceived;
    public event Action<S2C_RespawnResult>      OnRespawnResultReceived;

    // 채집 오브젝트
    public event Action<S2C_ObjectInfo>         OnObjectInfoReceived;
    public event Action<S2C_ObjectState>        OnObjectStateReceived;
    public event Action<S2C_InteractResult>     OnInteractResultReceived;

    public S2C_EnterFieldResult PendingEnterFieldResult { get; private set; }

    public S2C_EnterTownResult    PendingEnterTownResult    { get; private set; }
    public S2C_EnterDungeonResult PendingEnterDungeonResult { get; private set; }

    [PacketHandler(PacketId.S2C_EnterTownResult)]
    private void OnEnterTownResult(byte[] data)
    {
        var packet = S2C_EnterTownResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] EnterTown: success={packet.Success}, entityId={packet.EntityId}");
        if (packet.Success)
        {
            PendingEnterTownResult = packet;
            if (LoadingScreen.Instance != null)
                LoadingScreen.Instance.LoadScene("Town");
            else
                SceneManager.LoadScene("Town");
        }
    }

    [PacketHandler(PacketId.S2C_EnterDungeonResult)]
    private void OnEnterDungeonResult(byte[] data)
    {
        var packet = S2C_EnterDungeonResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] EnterDungeon: success={packet.Success}");
        if (packet.Success)
        {
            PendingEnterDungeonResult = packet;
            if (LoadingScreen.Instance != null)
                LoadingScreen.Instance.LoadScene("Dungeon");
            else
                SceneManager.LoadScene("Dungeon");
        }
        else
        {
            Debug.LogWarning($"[NetworkManager] EnterDungeon failed: {packet.Message}");
            OnEnterDungeonReceived?.Invoke(packet);
        }
    }

    [PacketHandler(PacketId.S2C_DungeonClear)]
    private void OnDungeonClear(byte[] data)
    {
        var packet = S2C_DungeonClear.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] DungeonClear: dungeonId={packet.DungeonId}, time={packet.ClearTimeSeconds}s");
        OnDungeonClearReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_LeaveDungeon)]
    private void OnLeaveDungeon(byte[] data)
    {
        Debug.Log("[NetworkManager] LeaveDungeon received");
        OnLeaveDungeonReceived?.Invoke();
    }

    [PacketHandler(PacketId.S2C_DungeonTimerUpdate)]
    private void OnDungeonTimerUpdate(byte[] data)
    {
        var packet = S2C_DungeonTimerUpdate.Parser.ParseFrom(data);
        OnDungeonTimerUpdateReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_EnterFieldResult)]
    private void OnEnterFieldResult(byte[] data)
    {
        var packet = S2C_EnterFieldResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] EnterField: success={packet.Success}, entityId={packet.EntityId}");
        if (packet.Success)
        {
            PendingEnterFieldResult = packet;
            if (LoadingScreen.Instance != null)
                LoadingScreen.Instance.LoadScene("Field");
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("Field");
        }
        else
        {
            Debug.LogWarning($"[NetworkManager] EnterField failed: {packet.Message}");
            OnEnterFieldReceived?.Invoke(packet);
        }
    }

    [PacketHandler(PacketId.S2C_LeaveField)]
    private void OnLeaveField(byte[] data)
    {
        Debug.Log("[NetworkManager] LeaveField received");
        OnLeaveFieldReceived?.Invoke();
    }

    [PacketHandler(PacketId.S2C_FieldQuotaUpdate)]
    private void OnFieldQuotaUpdate(byte[] data)
    {
        var packet = S2C_FieldQuotaUpdate.Parser.ParseFrom(data);
        OnFieldQuotaUpdateReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_RespawnResult)]
    private void OnRespawnResult(byte[] data)
    {
        var packet = S2C_RespawnResult.Parser.ParseFrom(data);
        OnRespawnResultReceived?.Invoke(packet);
    }

    public void SendEnterDungeon(int dungeonId)
    {
        Send(PacketId.C2S_EnterDungeon, new C2S_EnterDungeon { DungeonId = dungeonId });
    }

    public void SendLeaveDungeon()
    {
        Send(PacketId.C2S_LeaveDungeon, new C2S_LeaveDungeon());
    }

    public void SendEnterField(int fieldId)
    {
        Send(PacketId.C2S_EnterField, new C2S_EnterField { FieldId = fieldId });
    }

    public void SendLeaveField()
    {
        Send(PacketId.C2S_LeaveField, new C2S_LeaveField());
    }

    public void SendRespawn()
    {
        Send(PacketId.C2S_Respawn, new C2S_Respawn());
    }

    public void SendInteract(int targetObjectId)
    {
        Send(PacketId.C2S_Interact, new C2S_Interact { TargetObjectId = targetObjectId });
    }

    [PacketHandler(PacketId.S2C_ObjectInfo)]
    private void OnObjectInfo(byte[] data)
    {
        var packet = S2C_ObjectInfo.Parser.ParseFrom(data);
        OnObjectInfoReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_ObjectState)]
    private void OnObjectState(byte[] data)
    {
        var packet = S2C_ObjectState.Parser.ParseFrom(data);
        OnObjectStateReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_InteractResult)]
    private void OnInteractResult(byte[] data)
    {
        var packet = S2C_InteractResult.Parser.ParseFrom(data);
        OnInteractResultReceived?.Invoke(packet);
    }
}
