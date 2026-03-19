using UnityEngine;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<S2C_Spawn>   OnSpawnReceived;
    public event Action<S2C_Despawn> OnDespawnReceived;
    public event Action<S2C_Move>    OnMoveReceived;

    [PacketHandler(PacketId.S2C_Spawn)]
    private void OnSpawn(byte[] data)
    {
        var packet = S2C_Spawn.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Spawn: EntityId={packet.Entity?.EntityId}, Name={packet.Entity?.Name}");
        OnSpawnReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_Despawn)]
    private void OnDespawn(byte[] data)
    {
        var packet = S2C_Despawn.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Despawn: EntityId={packet.EntityId}");
        OnDespawnReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_Move)]
    private void OnMove(byte[] data)
    {
        var packet = S2C_Move.Parser.ParseFrom(data);
        OnMoveReceived?.Invoke(packet);
    }
}
