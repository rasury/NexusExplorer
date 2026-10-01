using System.IO;

namespace NexusExplorer.Models;

/// <summary>受管理的文件。数据库保存绝对路径,物理位置由整理操作决定。</summary>
public class FileItem
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>文件绝对路径。加入分类时不移动,整理时更新。</summary>
    public string AbsolutePath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Category? Category { get; set; }

    /// <summary>文件是否仍存在于磁盘上(外部移动/改名/删除会导致失效)。</summary>
    public bool ExistsOnDisk => File.Exists(AbsolutePath);

    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();
}
