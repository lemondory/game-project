using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;
using GameShared.Generated.Enums;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from ItemData.xlsx
/// </summary>
[MessagePackObject]
public partial class ItemData
{
    /// <summary>아이템id</summary>
    [Key(0)]
    public int ItemId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>등급</summary>
    [Key(2)]
    public ItemRarity Rarity { get; set; }

    /// <summary>가격</summary>
    [Key(3)]
    public int Price { get; set; }

    /// <summary>설명</summary>
    [Key(4)]
    public string Description { get; set; } = string.Empty;

}

/// <summary>
/// Auto-generated table class for ItemData
/// </summary>
[MessagePackObject]
public partial class ItemDataTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, ItemData> Data { get; set; } = new();

    public ItemData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<ItemData> GetAll() => Data.Values;

    public IEnumerable<ItemData> Where(Func<ItemData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
