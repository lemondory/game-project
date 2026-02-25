using System.Collections.Generic;
using MessagePack;
using GameShared.Enums;
using GameShared.Utils;

namespace GameShared.Packets;

[MessagePackObject]
public class C2S_EnterTown
{
}

[MessagePackObject]
public class S2C_EnterTownResult
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public long EntityId { get; set; }

    [Key(2)]
    public Vector3 Position { get; set; }

    [Key(3)]
    public List<EntityInfo> NearbyEntities { get; set; } = new();
}

[MessagePackObject]
public class C2S_EnterDungeon
{
    [Key(0)]
    public int DungeonId { get; set; }
}

[MessagePackObject]
public class S2C_EnterDungeonResult
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public long EntityId { get; set; }

    [Key(3)]
    public Vector3 Position { get; set; }

    [Key(4)]
    public List<EntityInfo> NearbyEntities { get; set; } = new();
}

[MessagePackObject]
public class S2C_LeaveDungeon
{
    [Key(0)]
    public bool Success { get; set; }
}

[MessagePackObject]
public class EntityInfo
{
    [Key(0)]
    public long EntityId { get; set; }

    [Key(1)]
    public EntityType EntityType { get; set; }

    [Key(2)]
    public string Name { get; set; } = string.Empty;

    [Key(3)]
    public Vector3 Position { get; set; }

    [Key(4)]
    public int CurrentHp { get; set; }

    [Key(5)]
    public int MaxHp { get; set; }
}
