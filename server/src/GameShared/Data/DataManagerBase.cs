using System;
using System.IO;
using MessagePack;

namespace GameShared.Data;

/// <summary>
/// Base class for data management
/// GameDataManager inherits from this class
/// </summary>
public abstract class DataManagerBase
{
    protected string DataPath { get; private set; } = string.Empty;
    public bool IsLoaded { get; private set; }

    protected DataManagerBase()
    {
        // Protected constructor for inheritance
    }

    /// <summary>
    /// Initialize and load all data tables
    /// </summary>
    /// <param name="dataPath">Directory containing .bytes files</param>
    public void Initialize(string dataPath)
    {
        if (IsLoaded)
        {
            Console.WriteLine("[GameDataManager] Already loaded. Skipping.");
            return;
        }

        DataPath = dataPath;

        if (!Directory.Exists(dataPath))
        {
            throw new DirectoryNotFoundException($"Data directory not found: {dataPath}");
        }

        Console.WriteLine($"[GameDataManager] Loading data from: {dataPath}");

        try
        {
            LoadAllTables();
            IsLoaded = true;
            Console.WriteLine("[GameDataManager] All data loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameDataManager] Failed to load data: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Reload all data tables
    /// </summary>
    public void Reload()
    {
        if (!IsLoaded)
        {
            Initialize(DataPath);
            return;
        }

        Console.WriteLine("[GameDataManager] Reloading data...");
        LoadAllTables();
        Console.WriteLine("[GameDataManager] Reload complete");
    }

    /// <summary>
    /// Clear all loaded data
    /// </summary>
    public void Clear()
    {
        ClearAllTables();
        IsLoaded = false;
        DataPath = string.Empty;
        Console.WriteLine("[GameDataManager] Data cleared");
    }

    /// <summary>
    /// Load a specific table from file
    /// </summary>
    protected T LoadTable<T>(string fileName) where T : class, IDataTable
    {
        var filePath = Path.Combine(DataPath, fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Data file not found: {filePath}");
        }

        var bytes = File.ReadAllBytes(filePath);
        var table = MessagePackSerializer.Deserialize<T>(bytes, MessagePackSerializerOptions.Standard);

        Console.WriteLine($"  ✓ Loaded {fileName} ({bytes.Length} bytes, {table.Count} records)");

        return table;
    }

    /// <summary>
    /// Override this to load all specific tables
    /// </summary>
    protected virtual void LoadAllTables()
    {
        // Override in partial class to load specific tables
    }

    /// <summary>
    /// Override this to clear all specific tables
    /// </summary>
    protected virtual void ClearAllTables()
    {
        // Override in partial class
    }
}
