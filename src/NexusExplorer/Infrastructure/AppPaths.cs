using System.IO;

namespace NexusExplorer.Infrastructure;

/// <summary>
/// 便携式路径解析:数据库、日志、分类根目录均位于应用程序目录旁,
/// 使软件可直接从 U 盘等任意目录运行。
/// </summary>
public static class AppPaths
{
    /// <summary>应用程序所在目录(便携布局的锚点)。</summary>
    public static string AppRoot
    {
        get
        {
            var exeDir = AppContext.BaseDirectory;
            // 开发调试时 exe 在 bin/Debug/net8.0-windows 下,向上取仓库内目录避免污染
            return exeDir;
        }
    }

    public static string DataDirectory => Path.Combine(AppRoot, "data");

    public static string DatabasePath => Path.Combine(DataDirectory, "nexus.db");

    public static string LogsDirectory => Path.Combine(AppRoot, "logs");

    /// <summary>默认分类物理根目录:软件目录下的 Storage。</summary>
    public static string DefaultStorageRoot => Path.Combine(AppRoot, "Storage");

    /// <summary>配置文件路径(应用目录旁)。</summary>
    public static string SettingsPath => Path.Combine(AppRoot, "appsettings.json");
}
