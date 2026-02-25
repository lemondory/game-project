using System;
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
    public void SerializeToMessagePack(TableSchema schema, string outputPath, HashSet<string> enumNames)
    {
        // Build dictionary with primary key
        var dictionary = new Dictionary<object, Dictionary<string, object?>>();

        foreach (var row in schema.Rows)
        {
            var primaryKeyValue = row[schema.PrimaryKey!.Name];
            if (primaryKeyValue == null)
                continue;

            // Process row data
            var processedRow = ProcessRow(row, schema, enumNames);

            dictionary[primaryKeyValue] = processedRow;
        }

        // Serialize to MessagePack
        var bytes = MessagePackSerializer.Serialize(dictionary, MessagePackSerializerOptions.Standard);

        File.WriteAllBytes(outputPath, bytes);
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

    private Dictionary<string, object?> ProcessRow(Dictionary<string, object?> row, TableSchema schema, HashSet<string> enumNames)
    {
        var processed = new Dictionary<string, object?>();

        // Process regular columns (skip ignored columns)
        foreach (var column in schema.Columns.Where(c => !c.IsArray && !c.IsIgnored))
        {
            var value = row.ContainsKey(column.Name) ? row[column.Name] : null;
            processed[column.Name] = ConvertValue(value, column, enumNames);
        }

        // Process array columns (skip ignored arrays)
        foreach (var arrayBaseName in schema.ArrayColumns.Keys)
        {
            var arrayColumns = schema.ArrayColumns[arrayBaseName];

            // Skip if any array column is ignored
            if (arrayColumns.Any(c => c.IsIgnored))
                continue;

            var arrayValues = new List<object?>();

            foreach (var arrayCol in arrayColumns)
            {
                var key = $"{arrayCol.Name}_{arrayCol.ArrayIndex}";
                var value = row.ContainsKey(key) ? row[key] : null;
                arrayValues.Add(ConvertValue(value, arrayCol, enumNames));
            }

            processed[arrayBaseName] = arrayValues.ToArray();
        }

        return processed;
    }

    private object? ConvertValue(object? value, ColumnInfo column, HashSet<string> enumNames)
    {
        if (value == null)
            return null;

        // Convert fixed-point values (use double for maximum precision)
        if (column.TypeName == "fixed")
        {
            double doubleValue = value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                _ => double.Parse(value.ToString()!)
            };

            // Convert to Fixed32 raw value (int) with proper rounding
            return (int)Math.Round(doubleValue * 10000);
        }

        // Enum values - convert string to int
        if (enumNames.Contains(column.TypeName) && value is string)
        {
            // For now, store as string - will be resolved during C# compilation
            return value;
        }

        return value;
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
