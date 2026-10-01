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

    public FileService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ---------- 查询 ----------

    public async Task<List<FileItem>> GetByCategoryAsync(int categoryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Files
            .AsNoTracking()
            .Where(f => f.CategoryId == categoryId)
            .OrderBy(f => f.FileName)
            .ToListAsync();
    }

    public async Task<FileItem?> GetByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
    }

    /// <summary>检查绝对路径是否已在数据库中(同一文件只允许属于一个分类)。</summary>
    public async Task<FileItem?> FindByPathAsync(string absolutePath)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var normalized = Path.GetFullPath(absolutePath);
        return await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.AbsolutePath == normalized);
    }

    // ---------- 添加 ----------

    /// <summary>
    /// 添加文件到分类。只写数据库,不移动物理文件。
    /// 若同一绝对路径已存在记录,则视为重新归类(更新 CategoryId)。
    /// </summary>
    public async Task<FileItem> AddAsync(string absolutePath, int categoryId)
    {
        if (!File.Exists(absolutePath))
            throw new OperationException($"文件不存在: {absolutePath}");

        await using var db = await _dbFactory.CreateDbContextAsync();

        _ = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("目标分类不存在。");

        var normalized = Path.GetFullPath(absolutePath);
        var existing = await db.Files.FirstOrDefaultAsync(f => f.AbsolutePath == normalized);

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
        db.Files.Add(file);
        await db.SaveChangesAsync();
        Log.Information("文件添加: {Path} -> 分类 {CategoryId}", normalized, categoryId);
        return file;
    }

    /// <summary>添加多个文件(拖入/多选)。逐个处理,失败的记入结果。</summary>
    public async Task<BatchAddResult> AddRangeAsync(IEnumerable<string> paths, int categoryId)
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

    /// <summary>递归导入文件夹下所有文件(不移动,只登记)。</summary>
    public async Task<BatchAddResult> ImportDirectoryAsync(string directory, int categoryId)
    {
        if (!Directory.Exists(directory))
            throw new OperationException($"文件夹不存在: {directory}");

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        return await AddRangeAsync(files, categoryId);
    }

    // ---------- 重新分类 ----------

    /// <summary>修改文件所属分类。物理文件不动,待整理时移动。</summary>
    public async Task RecategorizeAsync(int fileId, int targetCategoryId)
    {
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
    public async Task RelocateAsync(int fileId, string newAbsolutePath)
    {
        if (!File.Exists(newAbsolutePath))
            throw new OperationException($"文件不存在: {newAbsolutePath}");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId)
            ?? throw new OperationException("文件记录不存在。");

        var normalized = Path.GetFullPath(newAbsolutePath);
        var conflict = await db.Files.FirstOrDefaultAsync(f => f.AbsolutePath == normalized && f.Id != fileId);
        if (conflict is not null)
            throw new OperationException($"该路径已被文件「{conflict.FileName}」占用。");

        var oldPath = file.AbsolutePath;
        file.AbsolutePath = normalized;
        file.FileName = Path.GetFileName(normalized);
        file.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        Log.Information("文件重新定位: {Old} -> {New}", oldPath, normalized);
    }

    // ---------- 失效检测 ----------

    /// <summary>检测某分类下失效的文件。</summary>
    public async Task<List<FileItem>> GetMissingFilesAsync(int categoryId)
    {
        var files = await GetByCategoryAsync(categoryId);
        return files.Where(f => !f.ExistsOnDisk).ToList();
    }

    // ---------- 删除 ----------

    /// <summary>
    /// 移除文件:只删数据库记录,磁盘上的源文件保持原位不动,
    /// 也不进回收站。用于"不想再管理这个文件但保留文件本身"。
    /// </summary>
    public async Task RemoveAsync(int fileId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId)
            ?? throw new OperationException("文件不存在。");

        db.Files.Remove(file);
        await db.SaveChangesAsync();
        Log.Information("文件移除(保留源文件): {Path}", file.AbsolutePath);
    }

    /// <summary>删除文件:确认后送回收站,再删数据库记录。</summary>
    public async Task DeleteAsync(int fileId, IRecycleBinService recycleBin)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId)
            ?? throw new OperationException("文件不存在。");

        if (File.Exists(file.AbsolutePath))
        {
            if (!recycleBin.SendFileToRecycleBin(file.AbsolutePath))
                throw new OperationException($"无法将文件送入回收站: {file.FileName}");
        }

        db.Files.Remove(file);
        await db.SaveChangesAsync();
        Log.Information("文件删除: {Path}", file.AbsolutePath);
    }
}

public record BatchAddResult
{
    public List<string> Added { get; } = new();
    public List<(string FileName, string Error)> Failed { get; } = new();
}
