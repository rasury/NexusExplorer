namespace NexusExplorer.Models;

/// <summary>分类。每个分类对应一个真实物理目录。</summary>
public class Category
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>分类对应的物理目录绝对路径。</summary>
    public string PhysicalPath { get; set; } = string.Empty;

    /// <summary>同级排序,越小越靠前。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否钉在底栏快捷分类层。</summary>
    public bool IsPinned { get; set; }

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
