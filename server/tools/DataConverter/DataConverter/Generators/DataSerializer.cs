using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DataConverter.Models;
using MessagePack;

namespace DataConverter.Generators;

/// <summary>
/// Serializes table data to MessagePack binary and CSV
/// </summary>
public class DataSerializer
{
    /// <summary>
    /// Serialize table data to MessagePack binary format compatible with
    /// [MessagePackObject][Key(int)] generated C# classes.
    ///
    /// Output format:
    ///   TableClass (array[1])
    ///     └─ [Key(0)] Data dict (map{ primaryKey → DataClass })
    ///          └─ DataClass (array[n]) — each element at index = Key(n)
    /// </summary>
    public void SerializeToMessagePack(TableSchema schema, string outputPath,
        HashSet<string> enumNames, List<EnumSchema> enumSchemas)
    {
        // Build enum lookup: enumTypeName → (valueName → intValue)
        var enumLookup = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumSchema in enumSchemas)
        {
            var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in enumSchema.Values)
                values[ev.Name] = ev.Value;
            enumLookup[enumSchema.EnumName] = values;
        }

        // Build ordered column list — must match CodeGenerator's Key(n) assignment:
        //   regularColumns first (Key 0, 1, ...), then arrayBaseNames (Key m, m+1, ...)
        var regularColumns = schema.Columns
            .Where(c => !c.IsArray && !c.IsIgnored)
            .ToList();
        var arrayBaseNames = schema.ArrayColumns
            .Where(kvp => kvp.Value.All(c => !c.IsIgnored))
            .Select(kvp => kvp.Key)
            .ToList();
        int totalKeys = regularColumns.Count + arrayBaseNames.Count;

        // Build (primaryKey, valueArray) pairs in schema row order
        var rows = new List<(object pk, object?[] values)>();
        foreach (var row in schema.Rows)
        {
            var pkValue = row[schema.PrimaryKey!.Name];
            if (pkValue == null)
                continue;

            var arr = new object?[totalKeys];
            int idx = 0;

            // Regular columns
            foreach (var col in regularColumns)
            {
                var raw = row.ContainsKey(col.Name) ? row[col.Name] : null;
                arr[idx++] = ConvertValue(raw, col, enumNames, enumLookup);
            }

            // Array columns
            foreach (var baseName in arrayBaseNames)
            {
                var arrayCols = schema.ArrayColumns[baseName];
                var elems = new object?[arrayCols.Count];
                for (int i = 0; i < arrayCols.Count; i++)
                {
                    var arrayCol = arrayCols[i];
                    var key = $"{arrayCol.Name}_{arrayCol.ArrayIndex}";
                    var raw = row.ContainsKey(key) ? row[key] : null;
                    elems[i] = ConvertValue(raw, arrayCol, enumNames, enumLookup);
                }
                arr[idx++] = elems;
            }

            rows.Add((pkValue, arr));
        }

        // Write the MessagePack binary
        //
        // Conceptual structure (using MessagePackObject + Key(int)):
        //   [                       ← TableClass array (1 element)
        //     {                     ← Data = Dictionary<pk, DataClass>
        //       1: [v0, v1, ...],   ← DataClass array (totalKeys elements)
        //       2: [v0, v1, ...],
        //     }
        //   ]
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);

        writer.WriteArrayHeader(1);          // TableClass has exactly Key(0) = Data
        writer.WriteMapHeader(rows.Count);

        foreach (var (pk, values) in rows)
        {
            WriteValue(ref writer, pk);      // primary key (int, long, or string)
            writer.WriteArrayHeader(totalKeys);
            foreach (var val in values)
                WriteValue(ref writer, val);
        }

        writer.Flush();
        File.WriteAllBytes(outputPath, buffer.WrittenMemory.ToArray());
    }

    public void SerializeToCsv(TableSchema schema, string outputPath)
    {
        var sb = new StringBuilder();

        // Header row (column names) - skip ignored columns
        var columnNames = schema.Columns
            .Where(c => !c.IsIgnored && (!c.IsArray || c.ArrayIndex == 0))
            .Select(c => c.IsArray ? c.ArrayBaseName! : c.Name);

        sb.AppendLine(string.Join(",", columnNames));

        // Data rows
        foreach (var row in schema.Rows)
        {
            var values = new List<string>();

            foreach (var column in schema.Columns.Where(c => !c.IsIgnored && (!c.IsArray || c.ArrayIndex == 0)))
            {
                if (column.IsArray)
                {
                    // Collect all array elements
                    var arrayColumns = schema.ArrayColumns[column.ArrayBaseName!];

                    // Skip if ignored
                    if (arrayColumns.Any(c => c.IsIgnored))
                        continue;

                    var arrayValues = new List<string>();

                    foreach (var arrayCol in arrayColumns)
                    {
                        var key = $"{arrayCol.Name}_{arrayCol.ArrayIndex}";
                        var value = row.ContainsKey(key) ? row[key] : null;
                        arrayValues.Add(FormatCsvValue(value));
                    }

                    values.Add($"\"{string.Join(";", arrayValues)}\"");
                }
                else
                {
                    var value = row.ContainsKey(column.Name) ? row[column.Name] : null;
                    values.Add(FormatCsvValue(value));
                }
            }

            sb.AppendLine(string.Join(",", values));
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Convert a raw Excel value to its correct runtime type for serialization.
    /// </summary>
    private object? ConvertValue(object? value, ColumnInfo column,
        HashSet<string> enumNames, Dictionary<string, Dictionary<string, int>> enumLookup)
    {
        if (value == null)
            return null;

        // fixed-point: double → int (raw value * 10000)
        if (column.TypeName == "fixed")
        {
            double d = value switch
            {
                double dv => dv,
                float f => f,
                int i => i,
                long l => l,
                _ => double.Parse(value.ToString()!)
            };
            return (int)Math.Round(d * 10000);
        }

        // Enum: string name → int value
        if (enumNames.Contains(column.TypeName) && value is string enumStr)
        {
            if (enumLookup.TryGetValue(column.TypeName, out var evMap) &&
                evMap.TryGetValue(enumStr, out int enumInt))
                return enumInt;

            // Fallback: try parsing as int directly
            if (int.TryParse(enumStr, out int parsed))
                return parsed;

            return 0;
        }

        return value;
    }

    /// <summary>
    /// Write a single value to the MessagePack stream.
    /// Handles all primitive types produced by ExcelParser + ConvertValue.
    /// </summary>
    private static void WriteValue(ref MessagePackWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNil();
                break;
            case bool b:
                writer.Write(b);
                break;
            case byte bv:
                writer.Write(bv);
                break;
            case short s:
                writer.Write(s);
                break;
            case int i:
                writer.Write(i);
                break;
            case long l:
                writer.Write(l);
                break;
            case float f:
                writer.Write(f);
                break;
            case double d:
                writer.Write(d);
                break;
            case decimal dec:
                writer.Write((double)dec);
                break;
            case string str:
                writer.Write(str);
                break;
            case object?[] arr:
                writer.WriteArrayHeader(arr.Length);
                foreach (var item in arr)
                    WriteValue(ref writer, item);
                break;
            default:
                // Fallback: convert to int (handles boxed numeric edge cases)
                try { writer.Write(Convert.ToInt32(value)); }
                catch { writer.WriteNil(); }
                break;
        }
    }

    private string FormatCsvValue(object? value)
    {
        if (value == null)
            return "null";

        var str = value.ToString()!;

        // Escape quotes and wrap in quotes if contains comma, quote, or newline
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n'))
        {
            str = str.Replace("\"", "\"\"");
            return $"\"{str}\"";
        }

        return str;
    }
}
