using System.IO;

namespace NexusExplorer.Models;

/// <summary>受管理的文件。数据库保存绝对路径,物理位置由整理操作决定。</summary>
public class FileItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private bool _isCurrent;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>文件绝对路径。加入分类时不移动,整理时更新。</summary>
    public string AbsolutePath { get; set; } = string.Empty;
    public int? DirectoryLocationId { get; set; }
    public DirectoryLocation? Location { get; set; }
    public string? RelativePath { get; set; }
    public string? ExternalAbsolutePath { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Category? Category { get; set; }

    /// <summary>文件是否仍存在于磁盘上(外部移动/改名/删除会导致失效)。</summary>
    public bool ExistsOnDisk => File.Exists(AbsolutePath);

    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();
}
