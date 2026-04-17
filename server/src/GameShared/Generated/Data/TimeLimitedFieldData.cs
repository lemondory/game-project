using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from TimeLimitedFieldData.xlsx
/// </summary>
[MessagePackObject]
public partial class TimeLimitedFieldData
{
    /// <summary>필드ID</summary>
    [Key(0)]
    public int FieldId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>설명</summary>
    [Key(2)]
    public string Description { get; set; } = string.Empty;

    /// <summary>일일제한(분)</summary>
    [Key(3)]
    public int DailyLimitMinutes { get; set; }

    /// <summary>주간제한(분)</summary>
    [Key(4)]
    public int WeeklyLimitMinutes { get; set; }

    /// <summary>리스폰딜레이(초)</summary>
    [Key(5)]
    public int RespawnDelaySeconds { get; set; }

    /// <summary>몬스터ID_1</summary>
    [Key(6)]
    public int[] MonsterIds { get; set; } = System.Array.Empty<int>();

}

/// <summary>
/// Auto-generated table class for TimeLimitedFieldData
/// </summary>
[MessagePackObject]
public partial class TimeLimitedFieldDataTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, TimeLimitedFieldData> Data { get; set; } = new();

    public TimeLimitedFieldData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<TimeLimitedFieldData> GetAll() => Data.Values;

    public IEnumerable<TimeLimitedFieldData> Where(Func<TimeLimitedFieldData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
