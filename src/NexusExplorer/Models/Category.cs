namespace NexusExplorer.Models;

/// <summary>分类。每个分类对应一个真实物理目录。</summary>
public class Category : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>分类对应的物理目录绝对路径。</summary>
    public string PhysicalPath { get; set; } = string.Empty;

    /// <summary>同级排序,越小越靠前。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否钉在底栏快捷分类层。</summary>
    public bool IsPinned { get; set; }
    public int PinnedOrder { get; set; }
    private bool _isOrganized;
    /// <summary>仅在完整整理成功后置为 true；绑定或目录变更使其失效，不扫描文件推断。</summary>
    public bool IsOrganized
    {
        get => _isOrganized;
        set
        {
            if (!SetProperty(ref _isOrganized, value)) return;
            OnPropertyChanged(nameof(OrganizationStatusText));
            OnPropertyChanged(nameof(OrganizationStatusHint));
        }
    }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string OrganizationStatusText => IsOrganized ? "✓ 已整理" : "↗ 待整理";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string OrganizationStatusHint => IsOrganized
        ? "上次整理全部成功，本分类绑定文件已位于分类目录。软件外部的移动或删除不自动检测。"
        : "尚未确认，或绑定文件、分类目录已变动；可能有文件位于其他目录。完整整理成功后更新标识。";
    public int? DirectoryLocationId { get; set; }
    public DirectoryLocation? Location { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = new List<Category>();

    public ICollection<FileItem> Files { get; set; } = new List<FileItem>();

    /// <summary>该分类在树中的层级深度(根分类为 1)。</summary>
    public int Depth
    {
        get
        {
            var depth = 1;
            var parent = Parent;
            while (parent is not null)
            {
                depth++;
                parent = parent.Parent;
            }
            return depth;
        }
    }
}
