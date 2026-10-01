using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NexusExplorer.Data;
using NexusExplorer.Models;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>
/// 分类服务:创建/删除/重命名/移动/排序/树查询。
/// 数据库操作与物理目录操作保持同步:先文件系统成功,再提交数据库事务。
/// </summary>
public class CategoryService
{
    public const int MaxDepth = 10;

    private static readonly char[] InvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    private static string InvalidNameError => "分类名称不能包含 \\ / : * ? \" < > | 这些字符。";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    /// <summary>分类物理根目录。默认为软件目录下 Storage,测试中可注入临时目录。</summary>
    public string StorageRoot { get; set; } = Infrastructure.AppPaths.DefaultStorageRoot;

    public CategoryService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ---------- 查询 ----------

    public async Task<List<Category>> GetTreeAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var all = await db.Categories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync();

        var lookup = all.ToLookup(c => c.ParentId);
        foreach (var category in all)
            category.Children = lookup[category.Id]
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .ToList();

        return lookup[null].ToList();
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<Category>> GetChildrenAsync(int? parentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Categories
            .AsNoTracking()
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync();
    }

    /// <summary>从根到该分类的完整路径名称。</summary>
    public async Task<string> GetCategoryPathAsync(int categoryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var names = new List<string>();
        var current = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId);
        while (current is not null)
        {
            names.Insert(0, current.Name);
            current = current.ParentId is null
                ? null
                : await db.Categories.FirstOrDefaultAsync(c => c.Id == current.ParentId);
        }

        return names.Count == 0 ? string.Empty : string.Join(" / ", names);
    }

    /// <summary>获取分类及其全部后代。</summary>
    public async Task<List<Category>> GetSubtreeAsync(int rootId, bool includeRoot = true)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var all = await db.Categories.AsNoTracking().ToListAsync();
        var result = new List<Category>();
        CollectSubtree(all, rootId, result, includeRoot);
        return result;
    }

    private static void CollectSubtree(List<Category> all, int id, List<Category> result, bool include)
    {
        var category = all.FirstOrDefault(c => c.Id == id);
        if (category is null) return;
        if (include) result.Add(category);
        foreach (var child in all.Where(c => c.ParentId == id).OrderBy(c => c.SortOrder))
            CollectSubtree(all, child.Id, result, true);
    }

    // ---------- 创建 ----------

    /// <summary>
    /// 按目录相对结构确保分类链存在(镜像导入用):
    /// 在 baseCategoryId 下按 names 逐级查找,不存在则创建。
    /// 返回链末端的分类。名称冲突跳过(同名即视为同一分类)。
    /// </summary>
    public async Task<Category> EnsurePathAsync(int baseCategoryId, IReadOnlyList<string> names)
    {
        var currentId = (int?)baseCategoryId;
        Category? current = null;

        foreach (var name in names)
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0) continue;

            ValidateName(trimmed);
            await using var db = await _dbFactory.CreateDbContextAsync();

            var existing = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ParentId == currentId && c.Name == trimmed);
            if (existing is not null)
            {
                current = existing;
                currentId = existing.Id;
                continue;
            }

            current = await CreateAsync(trimmed, currentId);
            currentId = current.Id;
        }

        return current ?? (await GetByIdAsync(baseCategoryId))!;
    }

    public async Task<Category> CreateAsync(string name, int? parentId)
    {
        ValidateName(name);

        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var parent = parentId is null
            ? null
            : await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId)
              ?? throw new OperationException("父分类不存在。");

        // 层级检查:父分类深度 + 1 不得超过最大层数
        if (parent is not null)
        {
            var parentDepth = GetDepth(db, parent.Id);
            if (parentDepth + 1 > MaxDepth)
                throw new OperationException($"分类最多支持 {MaxDepth} 层。");
        }

        if (await db.Categories.AnyAsync(c => c.ParentId == parentId && c.Name == name))
            throw new OperationException("分类名称已存在");

        var rootPath = GetRootPath(db);
        var physicalPath = Path.Combine(parent?.PhysicalPath ?? rootPath, name);

        // 同级排序:追加到末尾
        var maxOrder = await db.Categories
            .Where(c => c.ParentId == parentId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync() ?? 0;

        Directory.CreateDirectory(physicalPath);

        var now = DateTime.Now;
        var category = new Category
        {
            Name = name,
            ParentId = parentId,
            PhysicalPath = physicalPath,
            SortOrder = maxOrder + 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Log.Information("分类创建: {Name} (物理目录: {Path})", name, physicalPath);
        return category;
    }

    // ---------- 重命名 ----------

    public async Task RenameAsync(int categoryId, string newName)
    {
        ValidateName(newName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("分类不存在。");

        if (category.Name == newName)
            return;

        if (await db.Categories.AnyAsync(c => c.ParentId == category.ParentId && c.Name == newName && c.Id != categoryId))
            throw new OperationException("分类名称已存在");

        var oldPath = category.PhysicalPath;
        var newPath = Path.Combine(
            Path.GetDirectoryName(oldPath) ?? GetRootPath(db),
            newName);

        if (Directory.Exists(oldPath))
        {
            if (Directory.Exists(newPath))
                throw new OperationException($"目标目录已存在: {newPath}");
            Directory.Move(oldPath, newPath);
        }

        // 更新自身及全部后代的 PhysicalPath 前缀
        var subtreeIds = new List<int>();
        CollectSubtreeIds(db, categoryId, subtreeIds);
        var descendants = await db.Categories.Where(c => subtreeIds.Contains(c.Id)).ToListAsync();

        category.Name = newName;
        category.PhysicalPath = newPath;
        category.UpdatedAt = DateTime.Now;
        foreach (var descendant in descendants.Where(d => d.Id != categoryId))
        {
            descendant.PhysicalPath = newPath + descendant.PhysicalPath.Substring(oldPath.Length);
            descendant.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Log.Information("分类重命名: {Old} -> {New} ({Path})", category.Name, newName, newPath);
    }

    // ---------- 移动 ----------

    /// <summary>把分类(含整个子树)移动到新的父分类下。null 表示移到顶层。</summary>
    public async Task MoveAsync(int categoryId, int? targetParentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("分类不存在。");

        if (category.ParentId == targetParentId)
            return;

        if (targetParentId == categoryId)
            throw new OperationException("不能把分类移动到自身下。");

        // 目标父分类存在性
        var targetParent = targetParentId is null
            ? null
            : await db.Categories.FirstOrDefaultAsync(c => c.Id == targetParentId)
              ?? throw new OperationException("目标分类不存在。");

        // 不允许移动到自己的后代
        if (targetParent is not null && IsDescendant(db, categoryId, targetParent.Id))
            throw new OperationException("不能把分类移动到它自己的子分类下。");

        // 深度检查
        var subtreeDepth = GetSubtreeMaxDepth(db, categoryId);
        var targetDepth = targetParent is null ? 0 : GetDepth(db, targetParent.Id);
        if (targetDepth + subtreeDepth > MaxDepth)
            throw new OperationException($"移动后超过最大 {MaxDepth} 层限制。");

        // 同级重名检查
        if (await db.Categories.AnyAsync(c => c.ParentId == targetParentId && c.Name == category.Name && c.Id != categoryId))
            throw new OperationException("目标分类下已存在同名分类");

        var rootPath = GetRootPath(db);
        var oldPath = category.PhysicalPath;
        var newPath = Path.Combine(targetParent?.PhysicalPath ?? rootPath, category.Name);

        // 物理目录同步移动
        if (Directory.Exists(oldPath))
        {
            if (Directory.Exists(newPath))
                throw new OperationException($"目标目录已存在: {newPath}");
            Directory.Move(oldPath, newPath);
        }

        // 更新子树所有 PhysicalPath
        var subtreeIds = new List<int>();
        CollectSubtreeIds(db, categoryId, subtreeIds);
        var descendants = await db.Categories.Where(c => subtreeIds.Contains(c.Id)).ToListAsync();

        category.ParentId = targetParentId;
        category.PhysicalPath = newPath;
        category.UpdatedAt = DateTime.Now;

        // 追加到目标层级末尾
        var maxOrder = await db.Categories
            .Where(c => c.ParentId == targetParentId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync() ?? 0;
        category.SortOrder = maxOrder + 1;

        foreach (var descendant in descendants.Where(d => d.Id != categoryId))
        {
            descendant.PhysicalPath = newPath + descendant.PhysicalPath.Substring(oldPath.Length);
            descendant.UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Log.Information("分类移动: {Name} -> {Target}", category.Name,
            targetParent is null ? "(顶层)" : targetParent.Name);
    }

    // ---------- 排序 ----------

    /// <summary>同级排序:把分类移动到新位置(0-based index)。</summary>
    public async Task ReorderAsync(int categoryId, int newIndex)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("分类不存在。");

        var siblings = await db.Categories
            .Where(c => c.ParentId == category.ParentId && c.Id != categoryId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        newIndex = Math.Clamp(newIndex, 0, siblings.Count);
        siblings.Insert(newIndex, category);

        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].SortOrder = i + 1;
            siblings[i].UpdatedAt = DateTime.Now;
        }

        await db.SaveChangesAsync();
        Log.Information("分类排序: {Name} -> 位置 {Index}", category.Name, newIndex + 1);
    }

    /// <summary>同级上移/下移。</summary>
    public async Task MoveWithinSiblingsAsync(int categoryId, int offset)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("分类不存在。");

        var siblings = await db.Categories
            .Where(c => c.ParentId == category.ParentId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        var index = siblings.FindIndex(c => c.Id == categoryId);
        var newIndex = Math.Clamp(index + offset, 0, siblings.Count - 1);
        if (index == newIndex) return;

        siblings.RemoveAt(index);
        siblings.Insert(newIndex, category);
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i + 1;

        await db.SaveChangesAsync();
    }

    // ---------- 删除 ----------

    /// <summary>
    /// 删除分类:回收站收纳分类下所有文件及物理目录,再删除数据库记录。
    /// 回收站失败时中止,保证数据库与文件系统一致。
    /// </summary>
    public async Task DeleteAsync(int categoryId, IRecycleBinService recycleBin)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("分类不存在。");

        var subtreeIds = new List<int>();
        CollectSubtreeIds(db, categoryId, subtreeIds);

        var files = await db.Files.Where(f => subtreeIds.Contains(f.CategoryId)).ToListAsync();

        // 1. 分类下所有文件送入回收站(仅限磁盘上仍存在的)
        foreach (var file in files)
        {
            if (File.Exists(file.AbsolutePath))
            {
                if (!recycleBin.SendFileToRecycleBin(file.AbsolutePath))
                    throw new OperationException($"无法将文件送入回收站: {file.FileName}");
            }
        }

        // 2. 物理目录送入回收站(目录本身包含整个子树的目录结构)
        if (Directory.Exists(category.PhysicalPath))
        {
            if (!recycleBin.SendDirectoryToRecycleBin(category.PhysicalPath))
                throw new OperationException($"无法将目录送入回收站: {category.PhysicalPath}");
        }

        // 3. 删除数据库记录
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Files.RemoveRange(files);
        var categories = await db.Categories.Where(c => subtreeIds.Contains(c.Id)).ToListAsync();
        db.Categories.RemoveRange(categories);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Log.Information("分类删除: {Name} (含 {FileCount} 个文件记录, {CategoryCount} 个分类记录)",
            category.Name, files.Count, categories.Count);
    }

    // ---------- 辅助 ----------

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new OperationException("分类名称不能为空。");
        name = name.Trim();
        if (name.IndexOfAny(InvalidNameChars) >= 0)
            throw new OperationException("分类名称不能包含 \\ / : * ? \" < > | 这些字符。");
        if (name is "." or "..")
            throw new OperationException("分类名称不能为 . 或 ..");
        if (name.Trim().Length > 100)
            throw new OperationException("分类名称过长。");
    }

    private string GetRootPath(AppDbContext db)
    {
        // 根分类的物理路径 = 根目录 \ 名称
        var firstRoot = db.Categories.AsNoTracking().FirstOrDefault(c => c.ParentId == null);
        if (firstRoot is not null)
        {
            var parent = Path.GetDirectoryName(firstRoot.PhysicalPath);
            if (!string.IsNullOrEmpty(parent)) return parent;
        }
        // 没有任何分类时使用配置的根目录
        return StorageRoot;
    }

    private static int GetDepth(AppDbContext db, int categoryId)
    {
        var depth = 1;
        var current = db.Categories.AsNoTracking().First(c => c.Id == categoryId);
        while (current.ParentId is not null)
        {
            depth++;
            current = db.Categories.AsNoTracking().First(c => c.Id == current.ParentId);
        }
        return depth;
    }

    private static int GetSubtreeMaxDepth(AppDbContext db, int rootId)
    {
        var all = db.Categories.AsNoTracking().ToList();
        int Max(int id)
        {
            var children = all.Where(c => c.ParentId == id).ToList();
            var depth = 1;
            foreach (var child in children)
                depth = Math.Max(depth, 1 + Max(child.Id));
            return depth;
        }
        return Max(rootId);
    }

    private static bool IsDescendant(AppDbContext db, int ancestorId, int candidateId)
    {
        var all = db.Categories.AsNoTracking().ToList();
        bool IsDesc(int id)
        {
            return all.Any(c => c.Id == id && c.ParentId == ancestorId)
                || all.Any(c => c.ParentId == ancestorId && IsDesc(c.Id));
        }
        // candidate 的祖先链中是否包含 ancestor
        var current = all.FirstOrDefault(c => c.Id == candidateId);
        while (current?.ParentId is not null)
        {
            if (current.ParentId == ancestorId) return true;
            current = all.FirstOrDefault(c => c.Id == current.ParentId);
        }
        return false;
    }

    private static void CollectSubtreeIds(AppDbContext db, int rootId, List<int> ids)
    {
        ids.Add(rootId);
        var children = db.Categories.AsNoTracking().Where(c => c.ParentId == rootId).Select(c => c.Id).ToList();
        foreach (var child in children)
            CollectSubtreeIds(db, child, ids);
    }
}
