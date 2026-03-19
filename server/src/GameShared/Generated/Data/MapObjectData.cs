using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;
using GameShared.Generated.Enums;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from MapObjectData.xlsx
/// </summary>
[MessagePackObject]
public partial class MapObjectData
{
    /// <summary>오브젝트ID</summary>
    [Key(0)]
    public int ObjectId { get; set; }

    /// <summary>존ID</summary>
    [Key(1)]
    public int ZoneId { get; set; }

    /// <summary>오브젝트타입</summary>
    [Key(2)]
    public ObjectType ObjectType { get; set; }

    /// <summary>참조ID</summary>
    [Key(3)]
    public int ReferenceId { get; set; }

    /// <summary>X좌표</summary>
    [Key(4)]
    public float PosX { get; set; }

    /// <summary>Y좌표</summary>
    [Key(5)]
    public float PosY { get; set; }

    /// <summary>Z좌표</summary>
    [Key(6)]
    public float PosZ { get; set; }

    /// <summary>프리팹키</summary>
    [Key(7)]
    public string PrefabKey { get; set; } = string.Empty;

    /// <summary>설명</summary>
    [Key(8)]
    public string Description { get; set; } = string.Empty;

}

/// <summary>
/// Auto-generated table class for MapObjectData
/// </summary>
[MessagePackObject]
public partial class MapObjectDataTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, MapObjectData> Data { get; set; } = new();

    public MapObjectData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<MapObjectData> GetAll() => Data.Values;

    public IEnumerable<MapObjectData> Where(Func<MapObjectData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
