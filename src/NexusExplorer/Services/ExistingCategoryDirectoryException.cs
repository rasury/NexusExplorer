namespace NexusExplorer.Services;

/// <summary>仅表示尚未绑定分类的现有目录；确认后需重新校验位置和分类记录。</summary>
public sealed class ExistingCategoryDirectoryException(string directoryPath)
    : OperationException("目录已存在，是否绑定？")
{
    public string DirectoryPath { get; } = directoryPath;
}
