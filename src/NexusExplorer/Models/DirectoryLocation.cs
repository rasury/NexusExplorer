using System.IO;
namespace NexusExplorer.Models;

/// <summary>Physical identity is independent of the logical category owning a file.</summary>
public sealed class DirectoryLocation
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public DirectoryLocation? Parent { get; set; }
    public string Segment { get; set; } = "";
    public string? RootPath { get; set; }
    public string Resolve() => Resolve(new HashSet<int>());
    private string Resolve(HashSet<int> visited)
    {
        if (!visited.Add(Id)) throw new InvalidDataException("目录位置存在循环。");
        if (RootPath is not null) return Path.GetFullPath(RootPath);
        if (Parent is null) throw new InvalidDataException("目录位置的父级缺失。");
        return Path.Combine(Parent.Resolve(visited), Segment);
    }
}
