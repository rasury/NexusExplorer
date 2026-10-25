using System.IO;
using Microsoft.VisualBasic.FileIO;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>基于 Microsoft.VisualBasic FileIO 的回收站实现(内部使用 SHFileOperation)。</summary>
public class RecycleBinService : IRecycleBinService
{
    public bool SendFileToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Log.Information("文件已送入回收站: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "文件送入回收站失败: {Path}", path);
            return false;
        }
    }

    public bool SendDirectoryToRecycleBin(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return false;

            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Log.Information("目录已送入回收站: {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "目录送入回收站失败: {Path}", path);
            return false;
        }
    }
}
