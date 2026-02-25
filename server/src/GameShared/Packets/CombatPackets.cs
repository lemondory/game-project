using MessagePack;

namespace GameShared.Packets;

[MessagePackObject]
public class C2S_Attack
{
    [Key(0)]
    public long TargetEntityId { get; set; }
}

[MessagePackObject]
public class S2C_Attack
{
    [Key(0)]
    public long AttackerEntityId { get; set; }

    [Key(1)]
    public long TargetEntityId { get; set; }
}

[MessagePackObject]
public class S2C_Damage
{
    [Key(0)]
    public long TargetEntityId { get; set; }

    [Key(1)]
    public int Damage { get; set; }

    [Key(2)]
    public int CurrentHp { get; set; }

    [Key(3)]
    public int MaxHp { get; set; }
}

[MessagePackObject]
public class S2C_Death
{
    [Key(0)]
    public long EntityId { get; set; }

    [Key(1)]
    public long KillerEntityId { get; set; }
}
