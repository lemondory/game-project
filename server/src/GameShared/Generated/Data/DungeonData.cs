using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;
using GameShared.Generated.Enums;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from DungeonData.xlsx
/// </summary>
[MessagePackObject]
public partial class DungeonData
{
    /// <summary>던전ID</summary>
    [Key(0)]
    public int DungeonId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>설명</summary>
    [Key(2)]
    public string Description { get; set; } = string.Empty;

    /// <summary>최소레벨</summary>
    [Key(3)]
    public int MinLevel { get; set; }

    /// <summary>최대인원</summary>
    [Key(4)]
    public int MaxPlayers { get; set; }

    /// <summary>던전타입</summary>
    [Key(5)]
    public DungeonType DungeonType { get; set; }

    /// <summary>제한시간(초)</summary>
    [Key(6)]
    public int TimeLimitSeconds { get; set; }

    /// <summary>리스폰딜레이(초)</summary>
    [Key(7)]
    public int RespawnDelaySeconds { get; set; }

    /// <summary>몬스터ID_1</summary>
    [Key(8)]
    public int[] MonsterIds { get; set; } = System.Array.Empty<int>();

}

/// <summary>
/// Auto-generated table class for DungeonData
/// </summary>
[MessagePackObject]
public partial class DungeonDataTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, DungeonData> Data { get; set; } = new();

    public DungeonData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<DungeonData> GetAll() => Data.Values;

    public IEnumerable<DungeonData> Where(Func<DungeonData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
