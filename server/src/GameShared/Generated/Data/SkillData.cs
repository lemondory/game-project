using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;
using GameShared.Generated.Enums;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from SkillData.xlsx
/// </summary>
[MessagePackObject]
public partial class SkillData
{
    /// <summary>스킬ID</summary>
    [Key(0)]
    public int SkillId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>타입</summary>
    [Key(2)]
    public SkillType Type { get; set; }

    /// <summary>쿨타임</summary>
    [Key(3)]
    public float Cooldown { get; set; }

    /// <summary>Lv1</summary>
    [Key(4)]
    public int[] Damage { get; set; } = System.Array.Empty<int>();

}

/// <summary>
/// Auto-generated table class for SkillData
/// </summary>
[MessagePackObject]
public partial class SkillDataTable
{
    [Key(0)]
    public Dictionary<int, SkillData> Data { get; set; } = new();

    public SkillData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<SkillData> GetAll() => Data.Values;

    public IEnumerable<SkillData> Where(Func<SkillData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
