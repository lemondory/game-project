using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using GameShared.Utils;

namespace GameShared.Generated.Data;

/// <summary>
/// Auto-generated data class from Strings.xlsx
/// </summary>
[MessagePackObject]
public partial class Strings
{
    [Key(0)]
    public int StringId { get; set; }

    [Key(1)]
    public string KO { get; set; } = string.Empty;

    [Key(2)]
    public string EN { get; set; } = string.Empty;

    [Key(3)]
    public string JP { get; set; } = string.Empty;

}

/// <summary>
/// Auto-generated table class for Strings
/// </summary>
[MessagePackObject]
public partial class StringsTable : GameShared.Data.IDataTable
{
    [Key(0)]
    public Dictionary<int, Strings> Data { get; set; } = new();

    public Strings? GetById(int id) => Data.GetValueOrDefault(id);

    public IEnumerable<Strings> GetAll() => Data.Values;

    public IEnumerable<Strings> Where(Func<Strings, bool> predicate)
        => Data.Values.Where(predicate);

    [IgnoreMember]
    public int Count => Data.Count;
}
