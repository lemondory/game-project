using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from MonsterData.xlsx
/// </summary>
[MessagePackObject]
public partial class MonsterData
{
    /// <summary>몬스터ID</summary>
    [Key(0)]
    public int MonsterId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>체력</summary>
    [Key(2)]
    public int Hp { get; set; }

}

/// <summary>
/// Auto-generated table class for MonsterData
/// </summary>
[MessagePackObject]
public partial class MonsterDataTable
{
    [Key(0)]
    public Dictionary<int, MonsterData> Data { get; set; } = new();

    public MonsterData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<MonsterData> GetAll() => Data.Values;

    public IEnumerable<MonsterData> Where(Func<MonsterData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
