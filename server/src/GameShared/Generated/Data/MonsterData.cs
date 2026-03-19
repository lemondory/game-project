using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;
using GameShared.Generated.Enums;

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

    /// <summary>레벨</summary>
    [Key(3)]
    public int Level { get; set; }

    /// <summary>타입</summary>
    [Key(4)]
    public MonsterType Type { get; set; }

    /// <summary>공격력</summary>
    [Key(5)]
    public int AttackPower { get; set; }

    /// <summary>방어력</summary>
    [Key(6)]
    public int Defense { get; set; }

    /// <summary>이동속도</summary>
    [Key(7)]
    public float MoveSpeed { get; set; }

    /// <summary>어그로범위(m)</summary>
    [Key(8)]
    public float AggroRange { get; set; }

    /// <summary>공격범위(m)</summary>
    [Key(9)]
    public float AttackRange { get; set; }

    /// <summary>공격쿨타임(초)</summary>
    [Key(10)]
    public float AttackCooldown { get; set; }

    /// <summary>경험치보상</summary>
    [Key(11)]
    public int ExpReward { get; set; }

    /// <summary>골드보상</summary>
    [Key(12)]
    public int GoldReward { get; set; }

    /// <summary>스킬ID_1</summary>
    [Key(13)]
    public int[] SkillIds { get; set; } = System.Array.Empty<int>();

}

/// <summary>
/// Auto-generated table class for MonsterData
/// </summary>
[MessagePackObject]
public partial class MonsterDataTable : GameShared.Data.IDataTable
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
