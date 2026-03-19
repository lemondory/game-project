using UnityEngine;
using GameShared.Enums;
using GameShared.Proto;
using System;

public partial class NetworkManager
{
    public event Action<S2C_Attack>       OnAttackReceived;
    public event Action<S2C_Damage>       OnDamageReceived;
    public event Action<S2C_Death>        OnDeathReceived;
    public event Action<S2C_RewardResult> OnRewardReceived;
    public event Action<S2C_LevelUp>      OnLevelUpReceived;

    [PacketHandler(PacketId.S2C_Attack)]
    private void OnAttack(byte[] data)
    {
        var packet = S2C_Attack.Parser.ParseFrom(data);
        OnAttackReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_Damage)]
    private void OnDamage(byte[] data)
    {
        var packet = S2C_Damage.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Damage: target={packet.TargetEntityId}, dmg={packet.Damage}, hp={packet.CurrentHp}/{packet.MaxHp}");
        OnDamageReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_Death)]
    private void OnDeath(byte[] data)
    {
        var packet = S2C_Death.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Death: entityId={packet.EntityId}, killer={packet.KillerEntityId}");
        OnDeathReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_RewardResult)]
    private void OnReward(byte[] data)
    {
        var packet = S2C_RewardResult.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] Reward: exp={packet.ExpReward}, gold={packet.GoldReward}");
        OnRewardReceived?.Invoke(packet);
    }

    [PacketHandler(PacketId.S2C_LevelUp)]
    private void OnLevelUp(byte[] data)
    {
        var packet = S2C_LevelUp.Parser.ParseFrom(data);
        Debug.Log($"[NetworkManager] LevelUp: level={packet.NewLevel}, maxHp={packet.NewMaxHp}");
        OnLevelUpReceived?.Invoke(packet);
    }
}
