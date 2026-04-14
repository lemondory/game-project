using UnityEngine;
using UnityEngine.SceneManagement;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<S2C_EnterDungeonResult> OnEnterDungeonReceived;

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

    public void SendEnterDungeon(int dungeonId)
    {
        Send(PacketId.C2S_EnterDungeon, new C2S_EnterDungeon { DungeonId = dungeonId });
    }
}
