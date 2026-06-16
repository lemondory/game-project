using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from WorldObjectLayout.xlsx
/// </summary>
[MessagePackObject]
public partial class WorldObjectLayout
{
    /// <summary>레이아웃ID</summary>
    [Key(0)]
    public int LayoutId { get; set; }

    /// <summary>필드ID</summary>
    [Key(1)]
    public int FieldId { get; set; }

    /// <summary>오브젝트데이터ID</summary>
    [Key(2)]
    public int ObjectDataId { get; set; }

    /// <summary>X좌표</summary>
    [Key(3)]
    public float PosX { get; set; }

    /// <summary>Y좌표</summary>
    [Key(4)]
    public float PosY { get; set; }

    /// <summary>Z좌표</summary>
    [Key(5)]
    public float PosZ { get; set; }

}

/// <summary>
/// Auto-generated table class for WorldObjectLayout
/// </summary>
[MessagePackObject]
public partial class WorldObjectLayoutTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, WorldObjectLayout> Data { get; set; } = new();

    public WorldObjectLayout? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<WorldObjectLayout> GetAll() => Data.Values;

    public IEnumerable<WorldObjectLayout> Where(Func<WorldObjectLayout, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
