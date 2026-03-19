using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataConverter.Generators;
using DataConverter.Models;
using DataConverter.Parsers;

namespace DataConverter;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== GameServer Data Converter ===\n");

        try
        {
            var options = ParseArguments(args);
            var converter = new DataConverter();
            converter.Run(options);

            Console.WriteLine("\n✓ Conversion complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Details: {ex.InnerException.Message}");
            }
            Console.WriteLine("\nUsage:");
            PrintUsage();
            Environment.Exit(1);
        }
    }

    static ConversionOptions ParseArguments(string[] args)
    {
        var options = new ConversionOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input":
                    options.InputDirectory = args[++i];
                    break;
                case "--output-code":
                    options.OutputCodeDirectory = args[++i];
                    break;
                case "--output-bytes":
                    options.OutputBytesDirectory = args[++i];
                    break;
                case "--output-csv":
                    options.OutputCsvDirectory = args[++i];
                    break;
                case "--enums":
                    options.EnumsFilePath = args[++i];
                    break;
                case "--file":
                    options.SingleFile = args[++i];
                    break;
                case "--validate-only":
                    options.ValidateOnly = true;
                    break;
                case "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }

        // Set defaults
        if (string.IsNullOrEmpty(options.InputDirectory) && string.IsNullOrEmpty(options.SingleFile))
        {
            options.InputDirectory = "data/xlsx";
        }

        if (string.IsNullOrEmpty(options.OutputCodeDirectory))
        {
            options.OutputCodeDirectory = "../../src/GameShared/Generated/Data";
        }

        if (string.IsNullOrEmpty(options.OutputBytesDirectory))
        {
            options.OutputBytesDirectory = "data/bytes";
        }

        if (string.IsNullOrEmpty(options.OutputCsvDirectory))
        {
            options.OutputCsvDirectory = "data/csv";
        }

        return options;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"
DataConverter - XLSX to C# and MessagePack converter

Usage:
  dotnet run -- [options]

Options:
  --input <dir>         Input directory containing XLSX files (default: data/xlsx)
  --output-code <dir>   Output directory for C# classes (default: ../../src/GameShared/Generated/Data)
  --output-bytes <dir>  Output directory for MessagePack files (default: data/bytes)
  --output-csv <dir>    Output directory for CSV files (default: data/csv)
  --enums <file>        Path to enums.xlsx file (default: <input>/enums.xlsx)
  --file <path>         Convert single file instead of directory
  --validate-only       Only validate files without generating output
  --help                Show this help message

Examples:
  dotnet run -- --input data/xlsx --output-code src/GameShared/Generated/Data
  dotnet run -- --file data/xlsx/MonsterData.xlsx
  dotnet run -- --validate-only
");
    }
}

class ConversionOptions
{
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputCodeDirectory { get; set; } = string.Empty;
    public string OutputBytesDirectory { get; set; } = string.Empty;
    public string OutputCsvDirectory { get; set; } = string.Empty;
    public string EnumsFilePath { get; set; } = string.Empty;
    public string SingleFile { get; set; } = string.Empty;
    public bool ValidateOnly { get; set; }
}

class DataConverter
{
    private readonly ExcelParser _parser = new();
    private readonly CodeGenerator _codeGenerator = new();
    private readonly DataSerializer _serializer = new();

    public void Run(ConversionOptions options)
    {
        // Parse enums first
        var enumSchemas = new List<EnumSchema>();
        var enumNames = new HashSet<string>();

        string enumsPath = string.IsNullOrEmpty(options.EnumsFilePath)
            ? Path.Combine(options.InputDirectory, "enums.xlsx")
            : options.EnumsFilePath;

        if (File.Exists(enumsPath))
        {
            Console.WriteLine($"Parsing enums: {enumsPath}");
            enumSchemas = _parser.ParseEnumFile(enumsPath);
            enumNames = new HashSet<string>(enumSchemas.Select(e => e.EnumName));
            Console.WriteLine($"  ✓ Loaded {enumSchemas.Count} enums");
        }

        // Get files to process
        var filesToProcess = new List<string>();

        if (!string.IsNullOrEmpty(options.SingleFile))
        {
            filesToProcess.Add(options.SingleFile);
        }
        else
        {
            if (!Directory.Exists(options.InputDirectory))
                throw new DirectoryNotFoundException($"Input directory not found: {options.InputDirectory}");

            filesToProcess = Directory.GetFiles(options.InputDirectory, "*.xlsx")
                .Where(f =>
                {
                    var fileName = Path.GetFileName(f);
                    return !fileName.StartsWith("~") &&
                           !fileName.Equals("enums.xlsx", StringComparison.OrdinalIgnoreCase) &&
                           !fileName.Equals("Enums.xlsx", StringComparison.OrdinalIgnoreCase) &&
                           !fileName.Equals("Consts.xlsx", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }

        Console.WriteLine($"\nFound {filesToProcess.Count} data file(s) to process");

        // Create output directories
        if (!options.ValidateOnly)
        {
            Directory.CreateDirectory(options.OutputCodeDirectory);
            Directory.CreateDirectory(options.OutputBytesDirectory);
            Directory.CreateDirectory(options.OutputCsvDirectory);
            Directory.CreateDirectory(Path.Combine(options.OutputCodeDirectory, "../Enums"));
        }

        // Generate enum code (all enums in a single file)
        if (!options.ValidateOnly && enumSchemas.Count > 0)
        {
            Console.WriteLine("\nGenerating enum code...");
            var allEnumsCode = _codeGenerator.GenerateAllEnumsCode(enumSchemas);
            var enumFilePath = Path.Combine(options.OutputCodeDirectory, "../Enums", "Enums.cs");
            File.WriteAllText(enumFilePath, allEnumsCode);
            Console.WriteLine($"  ✓ Enums.cs ({enumSchemas.Count} enums)");
        }

        // Parse and generate constants
        string constsPath = Path.Combine(options.InputDirectory, "Consts.xlsx");
        if (File.Exists(constsPath) && !options.ValidateOnly)
        {
            Console.WriteLine($"\nParsing constants: {constsPath}");
            var constSchema = _parser.ParseConstFile(constsPath);
            Console.WriteLine($"  ✓ Loaded {constSchema.Values.Count} constants");

            if (constSchema.Values.Count > 0)
            {
                var constCode = _codeGenerator.GenerateConstClass(constSchema);
                var constFilePath = Path.Combine(options.OutputCodeDirectory, "GameConsts.cs");
                File.WriteAllText(constFilePath, constCode);
                Console.WriteLine($"  ✓ Generated GameConsts.cs");
            }
        }

        // Process each data file
        int totalRecords = 0;
        var allSchemas = new List<TableSchema>();

        foreach (var file in filesToProcess)
        {
            Console.WriteLine($"\nProcessing: {Path.GetFileName(file)}");

            try
            {
                var schema = _parser.ParseDataFile(file);
                Console.WriteLine($"  ✓ {schema.Rows.Count} records");
                totalRecords += schema.Rows.Count;

                // Validation
                ValidateSchema(schema, enumNames);

                allSchemas.Add(schema);

                if (!options.ValidateOnly)
                {
                    // Generate C# classes (combined Data + Table in one file)
                    var combinedClass = _codeGenerator.GenerateCombinedDataClass(schema, enumNames);
                    var classPath = Path.Combine(options.OutputCodeDirectory, $"{schema.ClassName}.cs");

                    File.WriteAllText(classPath, combinedClass);

                    Console.WriteLine($"  ✓ Generated {schema.ClassName}.cs (Data + Table)");

                    // Serialize to MessagePack
                    var bytesPath = Path.Combine(options.OutputBytesDirectory, $"{schema.TableName}.bytes");
                    _serializer.SerializeToMessagePack(schema, bytesPath, enumNames, enumSchemas);
                    var bytesSize = new FileInfo(bytesPath).Length;
                    Console.WriteLine($"  ✓ Generated {schema.TableName}.bytes ({bytesSize} bytes)");

                    // Export to CSV
                    var csvPath = Path.Combine(options.OutputCsvDirectory, $"{schema.TableName}.csv");
                    _serializer.SerializeToCsv(schema, csvPath);
                    Console.WriteLine($"  ✓ Generated {schema.TableName}.csv");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Error: {ex.Message}");
                throw;
            }
        }

        // Generate GameDataManager partial class
        if (!options.ValidateOnly && allSchemas.Count > 0)
        {
            Console.WriteLine("\nGenerating GameDataManager...");
            var dataManagerCode = _codeGenerator.GenerateDataManagerClass(allSchemas);
            var dataManagerPath = Path.Combine(options.OutputCodeDirectory, "../Data", "GameDataManager.Generated.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(dataManagerPath)!);
            File.WriteAllText(dataManagerPath, dataManagerCode);

            Console.WriteLine($"  ✓ Generated GameDataManager.Generated.cs");
        }

        Console.WriteLine($"\n=== Summary ===");
        Console.WriteLine($"Files processed: {filesToProcess.Count}");
        Console.WriteLine($"Total records: {totalRecords}");
        Console.WriteLine($"Enums: {enumSchemas.Count}");
    }

    private void ValidateSchema(TableSchema schema, HashSet<string> enumNames)
    {
        // Check for duplicate primary keys
        var primaryKeyValues = new HashSet<object>();
        int rowIndex = 1;

        foreach (var row in schema.Rows)
        {
            var pkValue = row[schema.PrimaryKey!.Name];
            if (pkValue != null)
            {
                if (!primaryKeyValues.Add(pkValue))
                {
                    throw new InvalidOperationException($"Duplicate primary key value '{pkValue}' at row {rowIndex}");
                }
            }
            rowIndex++;
        }

        // Validate enum references
        foreach (var column in schema.Columns)
        {
            if (char.IsUpper(column.TypeName[0]) &&
                !column.TypeName.StartsWith("ref:") &&
                !enumNames.Contains(column.TypeName.TrimEnd('?')))
            {
                Console.WriteLine($"  ⚠ Warning: Enum type '{column.TypeName}' not found in enums.xlsx");
            }
        }
    }
}
