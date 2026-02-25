using System;
using System.Collections.Generic;
using System.IO;
using MessagePack;

namespace GameShared.Data;

/// <summary>
/// Manages game data loaded from MessagePack binary files
/// Singleton pattern for global access
/// Thread-safe initialization
/// </summary>
public class DataManager
{
    private static DataManager? _instance;
    private static readonly object _lock = new object();

    /// <summary>Singleton instance</summary>
    public static DataManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new DataManager();
                    }
                }
            }
            return _instance;
        }
    }

    // Data tables - Add your generated tables here
    private readonly Dictionary<Type, object> _tables = new Dictionary<Type, object>();
    private bool _isLoaded = false;
    private string _dataPath = string.Empty;

    private DataManager()
    {
        // Private constructor for singleton
    }

    /// <summary>
    /// Load all data tables from the specified directory
    /// </summary>
    /// <param name="dataPath">Directory containing .bytes files</param>
    public void LoadAll(string dataPath)
    {
        if (_isLoaded)
        {
            throw new InvalidOperationException("Data already loaded. Call Reload() to reload.");
        }

        _dataPath = dataPath;

        if (!Directory.Exists(dataPath))
        {
            throw new DirectoryNotFoundException($"Data directory not found: {dataPath}");
        }

        Console.WriteLine($"[DataManager] Loading data from: {dataPath}");

        // Load all .bytes files
        var bytesFiles = Directory.GetFiles(dataPath, "*.bytes");
        int successCount = 0;

        foreach (var file in bytesFiles)
        {
            try
            {
                LoadDataFile(file);
                successCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataManager] Failed to load {Path.GetFileName(file)}: {ex.Message}");
                throw;
            }
        }

        _isLoaded = true;
        Console.WriteLine($"[DataManager] Loaded {successCount}/{bytesFiles.Length} data files successfully");
    }

    /// <summary>
    /// Reload all data (for hot-reload support)
    /// </summary>
    public void Reload()
    {
        if (!_isLoaded)
        {
            throw new InvalidOperationException("Data not loaded yet. Call LoadAll() first.");
        }

        _tables.Clear();
        _isLoaded = false;
        LoadAll(_dataPath);
    }

    /// <summary>
    /// Get a data table by type
    /// </summary>
    public T GetTable<T>() where T : class
    {
        var type = typeof(T);

        if (_tables.TryGetValue(type, out var table))
        {
            return (T)table;
        }

        throw new InvalidOperationException($"Table {type.Name} not found. Did you load the data?");
    }

    /// <summary>
    /// Check if a table is loaded
    /// </summary>
    public bool HasTable<T>() where T : class
    {
        return _tables.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Clear all loaded data
    /// </summary>
    public void Clear()
    {
        _tables.Clear();
        _isLoaded = false;
        _dataPath = string.Empty;
    }

    private void LoadDataFile(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var bytes = File.ReadAllBytes(filePath);

        // Deserialize based on file name convention
        // Note: This requires reflection or manual registration of table types
        // For now, we'll use a generic approach

        try
        {
            // Use MessagePack's dynamic deserialization
            var data = MessagePackSerializer.Deserialize<object>(bytes, MessagePackSerializerOptions.Standard);

            // Store with file name as key for now
            // In production, you'd register specific types
            Console.WriteLine($"[DataManager] Loaded {fileName}: {bytes.Length} bytes");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to deserialize {fileName}", ex);
        }
    }

    /// <summary>
    /// Register a data table manually (typed approach)
    /// </summary>
    public void RegisterTable<T>(T table) where T : class
    {
        _tables[typeof(T)] = table;
    }

    /// <summary>
    /// Load a specific table from file
    /// </summary>
    public T LoadTable<T>(string filePath) where T : class
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Data file not found: {filePath}");
        }

        var bytes = File.ReadAllBytes(filePath);
        var table = MessagePackSerializer.Deserialize<T>(bytes, MessagePackSerializerOptions.Standard);

        _tables[typeof(T)] = table;

        Console.WriteLine($"[DataManager] Loaded {typeof(T).Name}: {bytes.Length} bytes");

        return table;
    }
}

/// <summary>
/// Extension methods for easier data access
/// Example: DataManager.Instance.Monsters() instead of GetTable<MonsterDataTable>()
/// </summary>
public static class DataManagerExtensions
{
    // Add extension methods for each table type
    // Example:
    // public static MonsterDataTable Monsters(this DataManager dm) => dm.GetTable<MonsterDataTable>();
    // public static ItemDataTable Items(this DataManager dm) => dm.GetTable<ItemDataTable>();
}
