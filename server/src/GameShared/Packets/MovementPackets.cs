using MessagePack;
using GameShared.Utils;

namespace GameShared.Packets;

[MessagePackObject]
public class C2S_Move
{
    [Key(0)]
    public Vector3 Destination { get; set; }
}

[MessagePackObject]
public class S2C_Move
{
    [Key(0)]
    public long EntityId { get; set; }

    [Key(1)]
    public Vector3 Position { get; set; }

    [Key(2)]
    public Vector3 Destination { get; set; }
}

[MessagePackObject]
public class S2C_Spawn
{
    [Key(0)]
    public EntityInfo Entity { get; set; } = new();
}

[MessagePackObject]
public class S2C_Despawn
{
    [Key(0)]
    public long EntityId { get; set; }
}
