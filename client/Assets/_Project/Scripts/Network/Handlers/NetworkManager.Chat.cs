using UnityEngine;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<S2C_Chat> OnChatReceived;

    [PacketHandler(PacketId.S2C_Chat)]
    private void OnChat(byte[] data)
    {
        var packet = S2C_Chat.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Chat: {packet.SenderName}: {packet.Message}");
        OnChatReceived?.Invoke(packet);
    }
}
