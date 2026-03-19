using UnityEngine;
using UnityEngine.SceneManagement;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<string> OnForceLogout;

    [PacketHandler(PacketId.S2C_LoginResult)]
    private void OnLoginResult(byte[] data)
    {
        var packet = S2C_LoginResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Login: success={packet.Success}, msg={packet.Message}");
        if (packet.Success)
            Send(PacketId.C2S_EnterTown, new C2S_EnterTown());
    }

    [PacketHandler(PacketId.S2C_ForceLogout)]
    private void OnForceLogoutPacket(byte[] data)
    {
        var packet = S2C_ForceLogout.Parser.ParseFrom(data);
        Debug.LogWarning($"[NetworkManager] ForceLogout: {packet.Message}");
        Disconnect();
        OnForceLogout?.Invoke(packet.Message);
        SceneManager.LoadScene("Login");
    }
}
