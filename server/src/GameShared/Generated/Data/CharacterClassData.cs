using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;
using GameShared.Generated.Enums;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from CharacterClassData.xlsx
/// </summary>
[MessagePackObject]
public partial class CharacterClassData
{
    /// <summary>클래스ID</summary>
    [Key(0)]
    public int ClassId { get; set; }

    /// <summary>이름</summary>
    [Key(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>기본HP</summary>
    [Key(2)]
    public int BaseHp { get; set; }

    /// <summary>기본공격력</summary>
    [Key(3)]
    public int BaseAttack { get; set; }

    /// <summary>기본방어력</summary>
    [Key(4)]
    public int BaseDefense { get; set; }

    /// <summary>이동속도</summary>
    [Key(5)]
    public float MoveSpeed { get; set; }

    /// <summary>레벨당HP증가</summary>
    [Key(6)]
    public int HpPerLevel { get; set; }

    /// <summary>레벨당공격력증가</summary>
    [Key(7)]
    public int AttackPerLevel { get; set; }

    /// <summary>레벨당방어력증가</summary>
    [Key(8)]
    public int DefensePerLevel { get; set; }

    /// <summary>캐릭터 타입</summary>
    [Key(9)]
    public CharacterType ClassType { get; set; }

}

/// <summary>
/// Auto-generated table class for CharacterClassData
/// </summary>
[MessagePackObject]
public partial class CharacterClassDataTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, CharacterClassData> Data { get; set; } = new();

    public CharacterClassData? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<CharacterClassData> GetAll() => Data.Values;

    public IEnumerable<CharacterClassData> Where(Func<CharacterClassData, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
