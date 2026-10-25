namespace NexusExplorer.Services;

/// <summary>Windows 回收站操作。抽象为接口以便单元测试替换。</summary>
public interface IRecycleBinService
{
    /// <summary>将文件送入回收站。成功返回 true。</summary>
    bool SendFileToRecycleBin(string path);

    /// <summary>将目录送入回收站。成功返回 true。</summary>
    bool SendDirectoryToRecycleBin(string path);
}
