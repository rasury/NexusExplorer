using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>
/// 文件服务:添加(不移动)、重新分类(只改 CategoryId)、
/// 失效检测、重新定位、递归导入、删除。
/// </summary>
public class FileService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    public Func<IReadOnlyCollection<string>, Task>? BeforePhysicalOperationAsync { get; set; }

    public FileService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ---------- 查询 ----------

    public Task<List<FileItem>> GetByCategoryAsync(int categoryId) => Task.Run(() => GetByCategoryAsyncCore(categoryId));
    private async Task<List<FileItem>> GetByCategoryAsyncCore(int categoryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var files = await db.Files
            .AsNoTracking()
            .Where(f => f.CategoryId == categoryId)
            .OrderBy(f => f.FileName).ThenBy(f => f.Id)
            .ToListAsync();
        await LocationService.ResolveAsync(db, files: files);
        return files;
    }

    public Task<FileItem?> GetByIdAsync(int id) => Task.Run(() => GetByIdAsyncCore(id));
    private async Task<FileItem?> GetByIdAsyncCore(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (file is not null) await LocationService.ResolveAsync(db, files: new[] { file });
        return file;
    }

    /// <summary>检查绝对路径是否已在数据库中(同一文件只允许属于一个分类)。</summary>
    public Task<FileItem?> FindByPathAsync(string absolutePath) => Task.Run(() => FindByPathAsyncCore(absolutePath));
    private async Task<FileItem?> FindByPathAsyncCore(string absolutePath)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var normalized = LocationService.Normalize(absolutePath);
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => EF.Functions.Collate(f.AbsolutePath, "NOCASE") == normalized);
        if (file is not null) await LocationService.ResolveAsync(db, files: new[] { file });
        return file;
    }

    // ---------- 添加 ----------

    /// <summary>
    /// 添加文件到分类。只写数据库,不移动物理文件。
    /// 若同一绝对路径已存在记录,则视为重新归类(更新 CategoryId)。
    /// </summary>
    public Task<FileItem> AddAsync(string absolutePath, int categoryId) => Task.Run(() => AddAsyncCore(absolutePath, categoryId));
    private async Task<FileItem> AddAsyncCore(string absolutePath, int categoryId)
    {
        using var lease = await MutationGate.AcquireAsync(_dbFactory);
        if (!File.Exists(absolutePath))
            throw new OperationException($"文件不存在: {absolutePath}");

        await using var db = await _dbFactory.CreateDbContextAsync();

        _ = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("目标分类不存在。");

        var normalized = LocationService.Normalize(absolutePath);
        var existing = await db.Files.FirstOrDefaultAsync(f => EF.Functions.Collate(f.AbsolutePath, "NOCASE") == normalized);

        var now = DateTime.Now;
        if (existing is not null)
        {
            existing.CategoryId = categoryId;
            existing.FileName = Path.GetFileName(normalized);
            existing.UpdatedAt = now;
            await db.SaveChangesAsync();
            Log.Information("文件重新归类(已存在记录): {Path} -> 分类 {CategoryId}", normalized, categoryId);
            return existing;
        }

        var file = new FileItem
        {
            CategoryId = categoryId,
            FileName = Path.GetFileName(normalized),
            AbsolutePath = normalized,
            CreatedAt = now,
            UpdatedAt = now
        };
        await LocationService.BindFileAsync(db, file);
        db.Files.Add(file);
        await db.SaveChangesAsync();
        Log.Information("文件添加: {Path} -> 分类 {CategoryId}", normalized, categoryId);
        return file;
    }

    /// <summary>添加多个文件(拖入/多选)。逐个处理,失败的记入结果。</summary>
    public Task<BatchAddResult> AddRangeAsync(IEnumerable<string> paths, int categoryId) => Task.Run(() => AddRangeAsyncCore(paths, categoryId));
    private async Task<BatchAddResult> AddRangeAsyncCore(IEnumerable<string> paths, int categoryId)
    {
        var result = new BatchAddResult();
        foreach (var path in paths)
        {
            try
            {
                await AddAsync(path, categoryId);
                result.Added.Add(Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "添加文件失败: {Path}", path);
                result.Failed.Add((Path.GetFileName(path), ex.Message));
            }
        }
        return result;
    }

    /// <summary>
    /// 镜像导入文件夹:按磁盘目录结构创建同名分类树
    /// (文件夹本身 → 同名子分类,子文件夹 → 再下一层,依此类推),
    /// 每个文件登记到其所在目录对应的分类。不移动任何物理文件。
    /// </summary>
    public Task<BatchAddResult> ImportDirectoryAsync(string directory, int categoryId, CategoryService categoryService) => Task.Run(() => ImportDirectoryAsyncCore(directory, categoryId, categoryService));
    private async Task<BatchAddResult> ImportDirectoryAsyncCore(string directory, int categoryId, CategoryService categoryService)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new OperationException("不递归导入目录联接点或符号链接。");
        if (!Directory.Exists(directory))
            throw new OperationException($"文件夹不存在: {directory}");

        var result = new BatchAddResult();
        var rootName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // 根目录下直接的文件 → 加入目标分类本身
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            try
            {
                await AddAsync(file, categoryId);
                result.Added.Add(Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "添加文件失败: {Path}", file);
                result.Failed.Add((Path.GetFileName(file), ex.Message));
            }
        }

        // 子目录递归:目录名 → 同名分类(不存在则创建),其下文件入该分类
        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            try
            {
                var subName = Path.GetFileName(sub);
                var subCategory = await categoryService.EnsurePathAsync(categoryId, new[] { subName });
                var subResult = await ImportDirectoryAsync(sub, subCategory.Id, categoryService);
                foreach (var added in subResult.Added) result.Added.Add(added);
                foreach (var failed in subResult.Failed) result.Failed.Add(failed);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "镜像导入子目录失败: {Path}", sub);
                result.Failed.Add((Path.GetFileName(sub), ex.Message));
            }
        }

        Serilog.Log.Information("镜像导入完成: {Directory} -> 分类 {CategoryId}, 添加 {Added} 个文件, 失败 {Failed} 个",
            directory, categoryId, result.Added.Count, result.Failed.Count);
        return result;
    }

    // ---------- 重新分类 ----------

    /// <summary>修改文件所属分类。物理文件不动,待整理时移动。</summary>
    public Task RecategorizeAsync(int fileId, int targetCategoryId) => Task.Run(() => RecategorizeAsyncCore(fileId, targetCategoryId));
    private async Task RecategorizeAsyncCore(int fileId, int targetCategoryId)
    {
        using var lease = await MutationGate.AcquireAsync(_dbFactory);
        await using var db = await _dbFactory.CreateDbContextAsync();

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId)
            ?? throw new OperationException("文件不存在。");

        if (file.CategoryId == targetCategoryId)
            return;

        _ = await db.Categories.FirstOrDefaultAsync(c => c.Id == targetCategoryId)
            ?? throw new OperationException("目标分类不存在。");

        var oldId = file.CategoryId;
        file.CategoryId = targetCategoryId;
        file.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        Log.Information("文件重新分类: {Path} {OldId} -> {NewId}", file.AbsolutePath, oldId, targetCategoryId);
    }

    // ---------- 重新定位 ----------

    /// <summary>外部移动/改名导致文件失效时,由用户手动指定新位置。</summary>
    public Task RelocateAsync(int fileId, string newAbsolutePath) => Task.Run(() => RelocateAsyncCore(fileId, newAbsolutePath));
    private async Task RelocateAsyncCore(int fileId, string newAbsolutePath)
    {
        using var lease = await MutationGate.AcquireAsync(_dbFactory);
        if (!File.Exists(newAbsolutePath))
            throw new OperationException($"文件不存在: {newAbsolutePath}");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId)
            ?? throw new OperationException("文件记录不存在。");

        var normalized = LocationService.Normalize(newAbsolutePath);
        var all = await db.Files.ToListAsync(); await LocationService.ResolveAsync(db, files: all);
        var conflict = all.FirstOrDefault(f => f.Id != fileId && string.Equals(f.AbsolutePath, normalized, StringComparison.OrdinalIgnoreCase));
        if (BeforePhysicalOperationAsync is not null) await BeforePhysicalOperationAsync(new[] { file.AbsolutePath });
        if (conflict is not null)
            throw new OperationException($"该路径已被文件「{conflict.FileName}」占用。");

        var oldPath = file.AbsolutePath;
        file.AbsolutePath = normalized;
        file.FileName = Path.GetFileName(normalized);
        file.UpdatedAt = DateTime.Now;
        await LocationService.BindFileAsync(db, file);
        await db.SaveChangesAsync();
        Log.Information("文件重新定位: {Old} -> {New}", oldPath, normalized);
    }

    // ---------- 失效检测 ----------

    /// <summary>检测某分类下失效的文件。</summary>
    public Task<List<FileItem>> GetMissingFilesAsync(int categoryId) => Task.Run(() => GetMissingFilesAsyncCore(categoryId));
    private async Task<List<FileItem>> GetMissingFilesAsyncCore(int categoryId)
    {
        var files = await GetByCategoryAsync(categoryId);
        return files.Where(f => !f.ExistsOnDisk).ToList();
    }

    // ---------- 删除 ----------

    /// <summary>
    /// 移除文件:只删数据库记录,磁盘上的源文件保持原位不动,
    /// 也不进回收站。用于"不想再管理这个文件但保留文件本身"。
    /// </summary>
    public Task RemoveAsync(int fileId) => Task.Run(() => RemoveAsyncCore(fileId));
    private async Task RemoveAsyncCore(int fileId)
    {
        using var lease = await MutationGate.AcquireAsync(_dbFactory);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId)
            ?? throw new OperationException("文件不存在。");

        db.Files.Remove(file);
        await db.SaveChangesAsync();
        Log.Information("文件移除(保留源文件): {Path}", file.AbsolutePath);
    }

    /// <summary>删除文件:确认后送回收站,再删数据库记录。</summary>
    public Task DeleteAsync(int fileId, IRecycleBinService recycleBin) => Task.Run(() => DeleteAsyncCore(fileId, recycleBin));
    private async Task DeleteAsyncCore(int fileId, IRecycleBinService recycleBin)
    {
        using var lease = await MutationGate.AcquireAsync(_dbFactory);
        var file = await GetByIdAsync(fileId) ?? throw new OperationException("文件不存在。");
        if (BeforePhysicalOperationAsync is not null) await BeforePhysicalOperationAsync(new[] { file.AbsolutePath });
        await new FileOperationExecutor(_dbFactory, recycleBin).DeleteFileAsync(fileId);
    }
}

public record BatchAddResult
{
    public List<string> Added { get; } = new();
    public List<(string FileName, string Error)> Failed { get; } = new();
}
