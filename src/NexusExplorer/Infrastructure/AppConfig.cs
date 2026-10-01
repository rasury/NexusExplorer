using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusExplorer.Infrastructure;

/// <summary>应用配置(JSON)。RootPath 为空时使用软件目录下 Storage。</summary>
public class AppConfig
{
    [JsonPropertyName("Storage")]
    public StorageConfig Storage { get; set; } = new();

    [JsonPropertyName("Database")]
    public DatabaseConfig Database { get; set; } = new();

    [JsonPropertyName("Logging")]
    public LoggingConfig Logging { get; set; } = new();

    public static AppConfig LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
                if (config is not null)
                    return config;
            }
        }
        catch
        {
            // 配置损坏时回退默认值,不打断启动
        }

        return new AppConfig();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    /// <summary>确保数据/日志目录存在。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database.ResolvedPath)!);
        Directory.CreateDirectory(Logging.ResolvedDirectory);
        Directory.CreateDirectory(Storage.ResolvedRoot);
    }
}

public class StorageConfig
{
    /// <summary>分类物理根目录。空 = 软件目录\Storage。</summary>
    [JsonPropertyName("RootPath")]
    public string? RootPath { get; set; }

    [JsonIgnore]
    public string ResolvedRoot =>
        string.IsNullOrWhiteSpace(RootPath)
            ? AppPaths.DefaultStorageRoot
            : RootPath!;
}

public class DatabaseConfig
{
    /// <summary>数据库文件路径。空 = 软件目录\data\nexus.db。</summary>
    [JsonPropertyName("Path")]
    public string? Path { get; set; }

    [JsonIgnore]
    public string ResolvedPath =>
        string.IsNullOrWhiteSpace(Path)
            ? AppPaths.DatabasePath
            : System.IO.Path.GetFullPath(Path)!;
}

public class LoggingConfig
{
    /// <summary>日志目录。空 = 软件目录\logs。</summary>
    [JsonPropertyName("Directory")]
    public string? Directory { get; set; }

    /// <summary>最低日志级别。</summary>
    [JsonPropertyName("MinimumLevel")]
    public string MinimumLevel { get; set; } = "Information";

    [JsonIgnore]
    public string ResolvedDirectory =>
        string.IsNullOrWhiteSpace(Directory)
            ? AppPaths.LogsDirectory
            : Directory!;
}
