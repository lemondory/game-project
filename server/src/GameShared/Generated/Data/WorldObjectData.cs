using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from WorldObjectData.xlsx
/// </summary>
[MessagePackObject]
public partial class WorldObjectData
{
    /// <summary>오브젝트ID</summary>
    [Key(0)]
    public int ObjectId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>리스폰딜레이(초)</summary>
    [Key(2)]
    public int RespawnDelaySeconds { get; set; }

    /// <summary>경험치보상</summary>
    [Key(3)]
    public int ExpReward { get; set; }

    /// <summary>아이템ID</summary>
    [Key(4)]
    public int ItemId { get; set; }

    /// <summary>아이템수량</summary>
    [Key(5)]
    public int ItemCount { get; set; }

    /// <summary>프리팹키</summary>
    [Key(6)]
    public string PrefabKey { get; set; } = string.Empty;

    /// <summary>설명</summary>
    [Key(7)]
    public string Description { get; set; } = string.Empty;

}

/// <summary>
/// Auto-generated table class for WorldObjectData
/// </summary>
[MessagePackObject]
public partial class WorldObjectDataTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, WorldObjectData> Data { get; set; } = new();

    public WorldObjectData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<WorldObjectData> GetAll() => Data.Values;

    public IEnumerable<WorldObjectData> Where(Func<WorldObjectData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
