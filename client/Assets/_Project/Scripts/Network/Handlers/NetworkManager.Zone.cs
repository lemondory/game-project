using UnityEngine;
using UnityEngine.SceneManagement;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<S2C_EnterDungeonResult> OnEnterDungeonReceived;
    public event Action<S2C_DungeonClear>       OnDungeonClearReceived;
    public event Action                         OnLeaveDungeonReceived;

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

    public void SendEnterDungeon(int dungeonId)
    {
        Send(PacketId.C2S_EnterDungeon, new C2S_EnterDungeon { DungeonId = dungeonId });
    }

    public void SendLeaveDungeon()
    {
        Send(PacketId.C2S_LeaveDungeon, new C2S_LeaveDungeon());
    }
}
