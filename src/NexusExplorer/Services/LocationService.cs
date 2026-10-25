using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;

namespace NexusExplorer.Services;

public static class LocationService
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool IsWithin(string path, string root)
    {
        var fullPath = Normalize(path); var fullRoot = Normalize(root);
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<List<DirectoryLocation>> LoadAsync(AppDbContext db)
    {
        var locations = await db.DirectoryLocations.ToListAsync();
        var lookup = locations.ToDictionary(l => l.Id);
        foreach (var location in locations)
            location.Parent = location.ParentId is int id && lookup.TryGetValue(id, out var parent) ? parent : null;
        return locations;
    }

    public static async Task ResolveAsync(AppDbContext db, IEnumerable<Category>? categories = null, IEnumerable<FileItem>? files = null)
    {
        var lookup = (await LoadAsync(db)).ToDictionary(l => l.Id);
        if (categories is not null)
            foreach (var category in categories)
                if (category.DirectoryLocationId is int id && lookup.TryGetValue(id, out var location))
                {
                    category.Location = location;
                    category.PhysicalPath = location.Resolve();
                }
        if (files is not null)
            foreach (var file in files)
            {
                if (file.DirectoryLocationId is int id && lookup.TryGetValue(id, out var location))
                {
                    file.Location = location;
                    file.AbsolutePath = Path.GetFullPath(Path.Combine(location.Resolve(), file.RelativePath ?? file.FileName));
                    if (!IsWithin(file.AbsolutePath, location.Resolve())) throw new InvalidDataException("文件相对路径越出目录位置。");
                }
                else if (file.ExternalAbsolutePath is not null) file.AbsolutePath = file.ExternalAbsolutePath;
            }
    }

    public static async Task BindFileAsync(AppDbContext db, FileItem file, IReadOnlyList<DirectoryLocation>? knownLocations = null)
    {
        var normalized = Normalize(file.AbsolutePath);
        var location = (knownLocations ?? await LoadAsync(db)).Where(l => IsWithin(normalized, l.Resolve()))
            .OrderByDescending(l => l.Resolve().Length).FirstOrDefault();
        file.AbsolutePath = normalized;
        file.DirectoryLocationId = location?.Id;
        file.Location = location;
        file.RelativePath = location is null ? null : Path.GetRelativePath(location.Resolve(), normalized);
        file.ExternalAbsolutePath = location is null ? normalized : null;
    }

    public static async Task InitializeLegacyLocationsAsync(AppDbContext db)
    {
        var categories = await db.Categories.ToListAsync();
        if (categories.GroupBy(c => Normalize(c.PhysicalPath), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("多个旧分类使用同一物理目录，需先明确位置映射，升级已停止。");
        var lookup = categories.ToDictionary(c => c.Id);
        var visiting = new HashSet<int>();
        async Task Ensure(Category c)
        {
            if (c.DirectoryLocationId is not null) return;
            if (!visiting.Add(c.Id)) throw new InvalidDataException("分类结构存在循环。");
            Category? parent = c.ParentId is int id && lookup.TryGetValue(id, out var p) ? p : null;
            if (c.ParentId is not null && parent is null) throw new InvalidDataException("分类父级缺失。");
            if (parent is not null) await Ensure(parent);
            var physicalParent = Path.GetDirectoryName(Normalize(c.PhysicalPath));
            var followsParent = parent is not null && string.Equals(physicalParent, Normalize(parent.PhysicalPath), StringComparison.OrdinalIgnoreCase);
            var location = new DirectoryLocation
            {
                ParentId = followsParent ? parent!.DirectoryLocationId : null,
                Segment = Path.GetFileName(Normalize(c.PhysicalPath)),
                RootPath = followsParent ? null : Normalize(c.PhysicalPath)
            };
            db.DirectoryLocations.Add(location);
            await db.SaveChangesAsync();
            c.DirectoryLocationId = location.Id;
            c.Location = location;
            await db.SaveChangesAsync();
            visiting.Remove(c.Id);
        }
        foreach (var category in categories) await Ensure(category);
        var files = await db.Files.ToListAsync();
        var locations = await LoadAsync(db);
        if (files.Any(f => !lookup.ContainsKey(f.CategoryId))) throw new InvalidDataException("文件所属分类缺失，升级已停止。");
        if (files.GroupBy(f => Normalize(f.AbsolutePath), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("旧数据库存在重复文件路径，升级已停止。");
        foreach (var file in files.Where(f => f.DirectoryLocationId is null && f.ExternalAbsolutePath is null))
            await BindFileAsync(db, file, locations);
        await db.SaveChangesAsync();
    }
}
