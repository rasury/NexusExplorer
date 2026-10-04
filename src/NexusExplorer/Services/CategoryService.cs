using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using Serilog;

namespace NexusExplorer.Services;

public sealed class CategoryService
{
    public const int MaxDepth = 10;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly FileOperationExecutor _operations;
    public string StorageRoot { get; set; } = Infrastructure.AppPaths.DefaultStorageRoot;
    public Func<IReadOnlyCollection<string>, Task>? BeforePhysicalOperationAsync { get; set; }
    public CategoryService(IDbContextFactory<AppDbContext> factory, FileOperationExecutor? operations = null)
    { _factory = factory; _operations = operations ?? new FileOperationExecutor(factory); }
    private async Task<List<Category>> AllAsync(AppDbContext db)
    {
        var all = await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
        await LocationService.ResolveAsync(db, categories: all); return all;
    }
    public Task<List<Category>> GetTreeAsync() => Task.Run(() => GetTreeAsyncCore());
    public Task<Dictionary<int, bool>> GetOrganizationStatesAsync() => Task.Run(async () =>
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Categories.AsNoTracking().Select(c => new { c.Id, c.IsOrganized }).ToDictionaryAsync(c => c.Id, c => c.IsOrganized);
    });
    private async Task<List<Category>> GetTreeAsyncCore()
    {
        await using var db = await _factory.CreateDbContextAsync(); var all = await AllAsync(db);
        var lookup = all.ToDictionary(c => c.Id); var children = all.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);
        foreach (var c in all) { c.Children = children[c.Id].ToList(); c.Parent = c.ParentId is int id ? lookup.GetValueOrDefault(id) : null; }
        return all.Where(c => c.ParentId is null).ToList();
    }
    public Task<Category?> GetByIdAsync(int id) => Task.Run(() => GetByIdAsyncCore(id));
    private async Task<Category?> GetByIdAsyncCore(int id)
    { await using var db = await _factory.CreateDbContextAsync(); return (await AllAsync(db)).FirstOrDefault(c => c.Id == id); }
    public Task<List<Category>> GetChildrenAsync(int? parentId) => Task.Run(() => GetChildrenAsyncCore(parentId));
    private async Task<List<Category>> GetChildrenAsyncCore(int? parentId)
    { await using var db = await _factory.CreateDbContextAsync(); return (await AllAsync(db)).Where(c => c.ParentId == parentId).ToList(); }
    public Task<string> GetCategoryPathAsync(int id) => Task.Run(() => GetCategoryPathAsyncCore(id));
    private async Task<string> GetCategoryPathAsyncCore(int id)
    {
        await using var db = await _factory.CreateDbContextAsync(); var all = await AllAsync(db);
        var names = new List<string>(); var seen = new HashSet<int>(); var current = all.FirstOrDefault(c => c.Id == id);
        while (current is not null)
        {
            if (!seen.Add(current.Id)) throw new OperationException("分类结构存在循环。");
            names.Insert(0, current.Name); current = all.FirstOrDefault(c => c.Id == current.ParentId);
        }
        return string.Join(" / ", names);
    }
    public Task<List<Category>> GetSubtreeAsync(int id, bool includeRoot = true) => Task.Run(() => GetSubtreeAsyncCore(id, includeRoot));
    private async Task<List<Category>> GetSubtreeAsyncCore(int id, bool includeRoot = true)
    {
        await using var db = await _factory.CreateDbContextAsync(); var all = await AllAsync(db); var ids = SubtreeIds(all, id);
        return all.Where(c => ids.Contains(c.Id) && (includeRoot || c.Id != id)).ToList();
    }
    private static HashSet<int> SubtreeIds(List<Category> all, int root)
    {
        var ids = new HashSet<int>(); var stack = new Stack<int>(); stack.Push(root);
        while (stack.TryPop(out var id))
        { if (!ids.Add(id)) throw new OperationException("分类结构存在循环。"); foreach (var c in all.Where(c => c.ParentId == id)) stack.Push(c.Id); }
        return ids;
    }
    private static int Depth(List<Category> all, Category? current)
    {
        var seen = new HashSet<int>(); var depth = 0;
        while (current is not null)
        { if (!seen.Add(current.Id)) throw new OperationException("分类结构存在循环。"); depth++; current = all.FirstOrDefault(c => c.Id == current.ParentId); }
        return depth;
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") throw new OperationException("分类名称不能为空或为 . / ..。");
        if (name.Length > 100 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new OperationException("分类名称包含 Windows 不支持的字符或结尾。");
        var first = name.Split('.')[0].ToUpperInvariant();
        if (first is "CON" or "PRN" or "AUX" or "NUL" || first.Length == 4 && (first.StartsWith("COM") || first.StartsWith("LPT")) && char.IsDigit(first[3]))
            throw new OperationException("分类名称不能使用 Windows 保留名称。");
    }
    public Task<Category> CreateAsync(string name, int? parentId) => Task.Run(() => CreateAsyncCore(name, parentId));
    public Task<Category> BindExistingDirectoryAsync(string name, int? parentId, string confirmedPath)
        => Task.Run(() => CreateAsyncCore(name, parentId, confirmedPath));
    private async Task<Category> CreateAsyncCore(string name, int? parentId, string? confirmedPath = null)
    {
        name = name.Trim(); ValidateName(name); using var lease = await MutationGate.AcquireAsync(_factory);
        await using var db = await _factory.CreateDbContextAsync(); var all = await AllAsync(db);
        var parent = all.FirstOrDefault(c => c.Id == parentId);
        if (parentId is not null && parent is null) throw new OperationException("父分类不存在。");
        if (Depth(all, parent) + 1 > MaxDepth) throw new OperationException("分类最多支持 10 层。");
        if (all.Any(c => c.ParentId == parentId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) throw new OperationException("分类名称已存在");
        var path = Path.GetFullPath(Path.Combine(parent?.PhysicalPath ?? StorageRoot, name));
        if (confirmedPath is not null && !string.Equals(path, Path.GetFullPath(confirmedPath), StringComparison.OrdinalIgnoreCase))
            throw new OperationException("分类目录位置已变化，请重新创建并确认绑定。");
        if (all.Any(c => string.Equals(Path.GetFullPath(c.PhysicalPath), path, StringComparison.OrdinalIgnoreCase)))
            throw new OperationException("此目录已绑定其他分类，不能重复绑定。");
        if (File.Exists(path)) throw new OperationException("目标位置已有同名文件，不能创建分类目录。");
        var existed = Directory.Exists(path);
        if (existed && confirmedPath is null) throw new ExistingCategoryDirectoryException(path);
        if (!existed && confirmedPath is not null) throw new OperationException("待绑定的目录已不存在，请重新创建分类。");
        if (existed && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new OperationException("不能绑定链接或联接目录，请选择实际目录。");
        if (!existed) Directory.CreateDirectory(path);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var location = (await LocationService.LoadAsync(db)).FirstOrDefault(l => string.Equals(l.Resolve(), path, StringComparison.OrdinalIgnoreCase));
            if (location is null)
            {
                location = new DirectoryLocation { ParentId = parent?.DirectoryLocationId, Parent = parent?.Location, Segment = name, RootPath = parent?.DirectoryLocationId is null ? path : null };
                db.DirectoryLocations.Add(location); await db.SaveChangesAsync();
            }
            var c = new Category { Name = name, ParentId = parentId, PhysicalPath = path, DirectoryLocationId = location.Id, Location = location,
                SortOrder = all.Where(c => c.ParentId == parentId).Select(c => c.SortOrder).DefaultIfEmpty().Max() + 1, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
            db.Categories.Add(c); await db.SaveChangesAsync(); await transaction.CommitAsync(); return c;
        }
        catch { if (!existed && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); throw; }
    }
    public Task<Category> EnsurePathAsync(int baseCategoryId, IReadOnlyList<string> names) => Task.Run(() => EnsurePathAsyncCore(baseCategoryId, names));
    private async Task<Category> EnsurePathAsyncCore(int baseCategoryId, IReadOnlyList<string> names)
    {
        var current = await GetByIdAsync(baseCategoryId) ?? throw new OperationException("分类不存在。");
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            current = (await GetChildrenAsync(current.Id)).FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? await CreateAsync(name, current.Id);
        return current;
    }
    public Task RenameAsync(int id, string newName) => Task.Run(() => RenameAsyncCore(id, newName));
    private async Task RenameAsyncCore(int id, string newName)
    {
        newName = newName.Trim(); ValidateName(newName); using var lease = await MutationGate.AcquireAsync(_factory);
        var c = await GetByIdAsync(id) ?? throw new OperationException("分类不存在。"); if (c.Name == newName) return;
        if ((await GetChildrenAsync(c.ParentId)).Any(x => x.Id != id && string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase))) throw new OperationException("分类名称已存在");
        if (string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            // Logical names are independent of physical folder spelling on Windows.
            await using var db = await _factory.CreateDbContextAsync();
            var tracked = await db.Categories.FirstAsync(x => x.Id == id);
            tracked.Name = newName; tracked.UpdatedAt = DateTime.Now; await db.SaveChangesAsync(); return;
        }
        await ChangeLocationAsync(c, Path.Combine(Path.GetDirectoryName(c.PhysicalPath)!, newName), false, newName, c.ParentId, preferRename: true);
    }
    public Task MoveAsync(int id, int? targetParentId) => Task.Run(() => MoveAsyncCore(id, targetParentId));
    private async Task MoveAsyncCore(int id, int? targetParentId)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); await using var db = await _factory.CreateDbContextAsync(); var all = await AllAsync(db);
        var c = all.FirstOrDefault(c => c.Id == id) ?? throw new OperationException("分类不存在。"); if (c.ParentId == targetParentId) return;
        var ids = SubtreeIds(all, id);
        if (targetParentId is int targetId && ids.Contains(targetId)) throw new OperationException("不能移到自身或子分类下。");
        var parent = all.FirstOrDefault(c => c.Id == targetParentId);
        if (targetParentId is not null && parent is null) throw new OperationException("目标分类不存在。");
        var subtreeDepth = all.Where(c => ids.Contains(c.Id)).Max(c => Depth(all, c)) - Depth(all, c) + 1;
        if (Depth(all, parent) + subtreeDepth > MaxDepth) throw new OperationException("移动后超过最大 10 层限制。");
        if (all.Any(x => x.ParentId == targetParentId && x.Id != id && string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase))) throw new OperationException("目标分类下已存在同名分类");
        await ChangeLocationAsync(c, Path.Combine(parent?.PhysicalPath ?? StorageRoot, c.Name), false, c.Name, targetParentId, preferRename: true);
    }
    public Task<List<string>> PreviewRelocateAsync(int id, string newDirectory) => Task.Run(() => PreviewRelocateAsyncCore(id, newDirectory));
    private async Task<List<string>> PreviewRelocateAsyncCore(int id, string newDirectory)
    {
        var category = await GetByIdAsync(id) ?? throw new OperationException("分类不存在。");
        await using var db = await _factory.CreateDbContextAsync(); var categories = await AllAsync(db); var files = await db.Files.ToListAsync(); await LocationService.ResolveAsync(db, files: files);
        return categories.Where(c => LocationService.IsWithin(c.PhysicalPath, category.PhysicalPath)).Select(c =>
            $"{c.Name}: {c.PhysicalPath} → {Path.Combine(newDirectory, Path.GetRelativePath(category.PhysicalPath, c.PhysicalPath))}")
            .Concat(files.Where(f => LocationService.IsWithin(f.AbsolutePath, category.PhysicalPath)).Select(f =>
            { var path = Path.Combine(newDirectory, Path.GetRelativePath(category.PhysicalPath, f.AbsolutePath)); return $"{f.FileName}: {path} ({(File.Exists(path) ? "存在" : "失效")})"; })).ToList();
    }
    public Task RelocateAsync(int id, string newDirectory) => Task.Run(() => RelocateAsyncCore(id, newDirectory));
    private async Task RelocateAsyncCore(int id, string newDirectory)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); var c = await GetByIdAsync(id) ?? throw new OperationException("分类不存在。");
        await ChangeLocationAsync(c, newDirectory, true, c.Name, c.ParentId);
    }
    public Task MigrateDirectoryAsync(int id, string targetParentDirectory) => Task.Run(() => MigrateDirectoryAsyncCore(id, targetParentDirectory));
    private async Task MigrateDirectoryAsyncCore(int id, string targetParentDirectory)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); var c = await GetByIdAsync(id) ?? throw new OperationException("分类不存在。");
        await ChangeLocationAsync(c, Path.Combine(targetParentDirectory, Path.GetFileName(c.PhysicalPath)), false, c.Name, c.ParentId);
    }
    private async Task ChangeLocationAsync(Category original, string target, bool relocateOnly, string newName, int? newParentId, bool preferRename = false)
    {
        var oldPath = original.PhysicalPath;
        target = LocationService.Normalize(target);
        await using (var check = await _factory.CreateDbContextAsync())
            if ((await AllAsync(check)).Any(c => c.Id != original.Id && string.Equals(c.PhysicalPath, target, StringComparison.OrdinalIgnoreCase)))
                throw new OperationException("目标目录已被另一个分类使用。");
        if (BeforePhysicalOperationAsync is not null) await BeforePhysicalOperationAsync(new[] { oldPath });
        await _operations.ChangeDirectoryAsync(oldPath, target, relocateOnly, async db =>
        {
            var all = await AllAsync(db); var files = await db.Files.ToListAsync(); await LocationService.ResolveAsync(db, files: files);
            var locations = await LocationService.LoadAsync(db);
            foreach (var file in files.Where(f => f.DirectoryLocationId is null && LocationService.IsWithin(f.AbsolutePath, oldPath)))
                await LocationService.BindFileAsync(db, file, locations);
            var oldLocations = locations.ToDictionary(l => l.Id, l => l.Resolve());
            var affectedLocations = oldLocations.Where(pair => LocationService.IsWithin(pair.Value, oldPath)).Select(pair => pair.Key).ToHashSet();
            var category = all.First(c => c.Id == original.Id);
            if (category.DirectoryLocationId is null) throw new OperationException("分类位置未初始化。");
            var root = locations.First(l => l.Id == category.DirectoryLocationId);
            root.ParentId = null; root.Parent = null; root.RootPath = LocationService.Normalize(target); root.Segment = Path.GetFileName(target);
            // An explicitly relocated descendant outside the physical source does not follow.
            foreach (var l in locations.Where(l => l.Id != root.Id && l.RootPath is not null && LocationService.IsWithin(oldLocations[l.Id], oldPath)))
                l.RootPath = Path.Combine(target, Path.GetRelativePath(oldPath, oldLocations[l.Id]));
            category.Name = newName; category.ParentId = newParentId;
            if (original.ParentId != newParentId) category.SortOrder = all.Where(c => c.ParentId == newParentId).Select(c => c.SortOrder).DefaultIfEmpty().Max() + 1;
            var locationLookup = locations.ToDictionary(l => l.Id);
            foreach (var c in all.Where(c => c.DirectoryLocationId is int id && affectedLocations.Contains(id)))
            { c.PhysicalPath = locationLookup[c.DirectoryLocationId!.Value].Resolve(); c.UpdatedAt = DateTime.Now; }
            foreach (var f in files.Where(f => f.DirectoryLocationId is int id && affectedLocations.Contains(id)))
            { f.AbsolutePath = Path.Combine(locationLookup[f.DirectoryLocationId!.Value].Resolve(), f.RelativePath!); f.UpdatedAt = DateTime.Now; }
            Log.Information("分类位置变更 {Old} -> {New}", oldPath, target);
        }, preferRename);
    }
    public Task<List<Category>> GetPinnedAsync() => Task.Run(() => GetPinnedAsyncCore());
    private async Task<List<Category>> GetPinnedAsyncCore()
    { await using var db = await _factory.CreateDbContextAsync(); return (await AllAsync(db)).Where(c => c.IsPinned).OrderBy(c => c.PinnedOrder).ThenBy(c => c.Id).ToList(); }
    public Task PinAsync(int id) => Task.Run(() => PinAsyncCore(id));
    private async Task PinAsyncCore(int id)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Categories.FirstAsync(c => c.Id == id); if (c.IsPinned) return;
        c.IsPinned = true; c.PinnedOrder = (await db.Categories.MaxAsync(c => (int?)c.PinnedOrder) ?? 0) + 1; await db.SaveChangesAsync();
    }
    public Task UnpinAsync(int id) => Task.Run(() => UnpinAsyncCore(id));
    public Task UnpinManyAsync(IReadOnlyCollection<int> categoryIds)
    {
        var ids = categoryIds.Distinct().ToArray();
        return Task.Run(async () =>
        {
            if (ids.Length == 0) return;
            using var lease = await MutationGate.AcquireAsync(_factory);
            await using var db = await _factory.CreateDbContextAsync();
            // One SQL statement commits the batch atomically; no per-label refresh or disk operation.
            await db.Categories.Where(c => ids.Contains(c.Id) && c.IsPinned)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.IsPinned, false));
        });
    }
    private async Task UnpinAsyncCore(int id)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Categories.FirstAsync(c => c.Id == id); c.IsPinned = false; await db.SaveChangesAsync();
    }
    public Task ReorderPinnedAsync(int id, int index) => Task.Run(() => ReorderPinnedAsyncCore(id, index));
    private async Task ReorderPinnedAsyncCore(int id, int index)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); await using var db = await _factory.CreateDbContextAsync();
        var pinned = await db.Categories.Where(c => c.IsPinned).OrderBy(c => c.PinnedOrder).ThenBy(c => c.Id).ToListAsync();
        var c = pinned.FirstOrDefault(c => c.Id == id) ?? throw new OperationException("快捷分类不存在。"); pinned.Remove(c); pinned.Insert(Math.Clamp(index, 0, pinned.Count), c);
        for (var i = 0; i < pinned.Count; i++) pinned[i].PinnedOrder = i + 1; await db.SaveChangesAsync();
    }
    public Task ReorderAsync(int id, int index) => Task.Run(() => ReorderAsyncCore(id, index));
    private async Task ReorderAsyncCore(int id, int index)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Categories.FirstAsync(c => c.Id == id);
        var siblings = await db.Categories.Where(x => x.ParentId == c.ParentId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        siblings.Remove(c); siblings.Insert(Math.Clamp(index, 0, siblings.Count), c);
        for (var i = 0; i < siblings.Count; i++) { siblings[i].SortOrder = i + 1; siblings[i].UpdatedAt = DateTime.Now; } await db.SaveChangesAsync();
    }
    public Task MoveWithinSiblingsAsync(int id, int offset) => Task.Run(() => MoveWithinSiblingsAsyncCore(id, offset));
    private async Task MoveWithinSiblingsAsyncCore(int id, int offset)
    {
        var c = await GetByIdAsync(id) ?? throw new OperationException("分类不存在。"); var siblings = await GetChildrenAsync(c.ParentId);
        await ReorderAsync(id, Math.Clamp(siblings.FindIndex(x => x.Id == id) + offset, 0, siblings.Count - 1));
    }
    /// <summary>Remove the logical subtree and its file registrations, preserving every physical path.</summary>
    public Task<(int ChildCount, int FileCount)> GetRemovalSummaryAsync(int id) => Task.Run(async () =>
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.Categories.AsNoTracking().ToListAsync();
        if (!all.Any(c => c.Id == id)) throw new OperationException("分类不存在。");
        var ids = SubtreeIds(all, id);
        // Count in SQLite instead of loading every file and resolving its path for the prompt.
        return (ids.Count - 1, await db.Files.CountAsync(f => ids.Contains(f.CategoryId)));
    });
    public Task<List<int>> RemoveAsync(int id) => Task.Run(() => RemoveAsyncCore(id));
    private async Task<List<int>> RemoveAsyncCore(int id)
    {
        using var lease = await MutationGate.AcquireAsync(_factory);
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.Categories.ToListAsync();
        var root = all.FirstOrDefault(c => c.Id == id) ?? throw new OperationException("分类不存在。");
        var ids = SubtreeIds(all, id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var files = db.Files.Where(f => ids.Contains(f.CategoryId));
        var removedFileIds = await files.Select(f => f.Id).ToListAsync();
        await files.ExecuteDeleteAsync();
        // Foreign keys restrict parent deletion; commit all levels together or roll everything back.
        foreach (var layer in all.Where(c => ids.Contains(c.Id)).GroupBy(c => Depth(all, c)).OrderByDescending(g => g.Key))
        {
            db.Categories.RemoveRange(layer);
            await db.SaveChangesAsync();
        }
        // Directory identities can still be referenced by files belonging to another category.
        await transaction.CommitAsync();
        Log.Information("分类移除(保留物理目录和文件): {Name};分类数 {Categories};文件登记数 {Files}", root.Name, ids.Count, removedFileIds.Count);
        return removedFileIds;
    }
    public List<string> LastDeleteWarnings { get; } = new();
    public Task DeleteAsync(int id, IRecycleBinService recycleBin) => Task.Run(() => DeleteAsyncCore(id, recycleBin));
    private async Task DeleteAsyncCore(int id, IRecycleBinService recycleBin)
    {
        using var lease = await MutationGate.AcquireAsync(_factory); LastDeleteWarnings.Clear(); var subtree = await GetSubtreeAsync(id); var ids = subtree.Select(c => c.Id).ToList();
        await using var db = await _factory.CreateDbContextAsync(); var files = await db.Files.Where(f => ids.Contains(f.CategoryId)).ToListAsync(); await LocationService.ResolveAsync(db, files: files);
        if (BeforePhysicalOperationAsync is not null) await BeforePhysicalOperationAsync(files.Select(f => f.AbsolutePath).ToList());
        var executor = new FileOperationExecutor(_factory, recycleBin); foreach (var f in files) await executor.DeleteFileAsync(f.Id);
        foreach (var c in subtree.OrderByDescending(c => c.PhysicalPath.Length))
        {
            if (!Directory.Exists(c.PhysicalPath)) continue;
            if (Directory.EnumerateFileSystemEntries(c.PhysicalPath).Any()) LastDeleteWarnings.Add($"保留非空目录: {c.PhysicalPath}");
            else if (!recycleBin.SendDirectoryToRecycleBin(c.PhysicalPath)) throw new OperationException("空目录回收失败，分类记录保留。");
        }
        db.ChangeTracker.Clear();
        var categories = await db.Categories.Where(c => ids.Contains(c.Id)).ToListAsync();
        foreach (var c in categories.OrderByDescending(c => Depth(categories, c))) { db.Categories.Remove(c); await db.SaveChangesAsync(); }
    }
}
