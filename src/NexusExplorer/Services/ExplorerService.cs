using System.Diagnostics;
using System.IO;

namespace NexusExplorer.Services;

/// <summary>资源管理器集成:打开目录 / 定位并选中文件。</summary>
public static class ExplorerService
{
    /// <summary>在资源管理器中打开目录。返回是否成功启动。</summary>
    public static bool OpenDirectory(string path)
    {
        if (!Directory.Exists(path))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "打开目录失败: {Path}", path);
            return false;
        }
    }

    /// <summary>在资源管理器中定位并选中文件。返回是否成功启动。</summary>
    public static bool RevealFile(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            // explorer /select 定位并选中;路径必须用反斜杠
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "定位文件失败: {Path}", path);
            return false;
        }
    }
}
