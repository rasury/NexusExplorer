using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>整理时同名文件冲突的处理方式。</summary>
public enum ConflictResolution
{
    Ask,
    Replace,
    Skip,
    KeepBoth
}

/// <summary>单个文件的整理结果。</summary>
public record OrganizeFileResult
{
    public string FileName { get; init; } = string.Empty;
    public string? OldPath { get; init; }
    public string? NewPath { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public OrganizeOutcome Outcome { get; init; }

    public static OrganizeFileResult Moved(string name, string oldPath, string newPath) =>
        new() { FileName = name, OldPath = oldPath, NewPath = newPath, Success = true, Outcome = OrganizeOutcome.Moved };

    public static OrganizeFileResult AlreadyOrganized(string name, string path) =>
        new() { FileName = name, OldPath = path, NewPath = path, Success = true, Outcome = OrganizeOutcome.AlreadyOrganized };

    public static OrganizeFileResult Missing(string name, string path) =>
        new() { FileName = name, OldPath = path, Success = false, Error = "源文件不存在", Outcome = OrganizeOutcome.Missing };

    public static OrganizeFileResult Skipped(string name, string path) =>
        new() { FileName = name, OldPath = path, Success = true, Outcome = OrganizeOutcome.Skipped };

    public static OrganizeFileResult Renamed(string name, string oldPath, string newPath) =>
        new() { FileName = name, OldPath = oldPath, NewPath = newPath, Success = true, Outcome = OrganizeOutcome.Renamed };

    public static OrganizeFileResult Failed(string name, string path, string error) =>
        new() { FileName = name, OldPath = path, Success = false, Error = error, Outcome = OrganizeOutcome.Failed };
}

public enum OrganizeOutcome
{
    Moved,
    AlreadyOrganized,
    Missing,
    Skipped,
    Renamed,
    Failed
}

/// <summary>
/// 整理服务:把当前分类中的文件实际移动到分类对应的物理目录。
/// 只整理当前分类,不递归子分类。
/// 文件移动成功后才更新数据库 AbsolutePath;失败绝不更新。
/// </summary>
public class OrganizationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OrganizationService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>支持 UI 干预的整理入口:遇到冲突时回调由调用方决定处理方式。</summary>
    public async Task<List<OrganizeFileResult>> OrganizeAsync(
        int categoryId,
        Func<string, string, Task<ConflictResolution>>? conflictHandler = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId)
            ?? throw new OperationException("分类不存在。");

        var files = await db.Files
            .Where(f => f.CategoryId == categoryId)
            .OrderBy(f => f.FileName)
            .ToListAsync();

        var targetDir = category.PhysicalPath;
        Directory.CreateDirectory(targetDir);

        var results = new List<OrganizeFileResult>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await OrganizeOneAsync(db, file, targetDir, conflictHandler));
        }

        await db.SaveChangesAsync();

        var moved = results.Count(r => r.Outcome is OrganizeOutcome.Moved or OrganizeOutcome.Renamed);
        Log.Information("整理完成: 分类 {Category}, 共 {Total} 个文件, 移动 {Moved} 个, 跳过 {Skipped} 个, 失败 {Failed} 个",
            category.Name, files.Count, moved,
            results.Count(r => r.Outcome is OrganizeOutcome.Skipped or OrganizeOutcome.AlreadyOrganized),
            results.Count(r => r.Outcome is OrganizeOutcome.Failed or OrganizeOutcome.Missing));

        return results;
    }

    private static async Task<OrganizeFileResult> OrganizeOneAsync(
        AppDbContext db,
        FileItem file,
        string targetDir,
        Func<string, string, Task<ConflictResolution>>? conflictHandler)
    {
        // 源文件已失效:跳过,不更新数据库
        if (!File.Exists(file.AbsolutePath))
            return OrganizeFileResult.Missing(file.FileName, file.AbsolutePath);

        var sourcePath = file.AbsolutePath;
        var targetPath = Path.Combine(targetDir, Path.GetFileName(sourcePath));

        // 已在目标位置
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            return OrganizeFileResult.AlreadyOrganized(file.FileName, sourcePath);

        // 冲突处理
        var resolution = ConflictResolution.Ask;
        if (File.Exists(targetPath))
        {
            if (conflictHandler is not null)
            {
                resolution = await conflictHandler(file.FileName, targetPath);
            }
            else
            {
                // 无交互回调时默认跳过,保证不误删
                resolution = ConflictResolution.Skip;
            }

            switch (resolution)
            {
                case ConflictResolution.Skip:
                    return OrganizeFileResult.Skipped(file.FileName, sourcePath);

                case ConflictResolution.KeepBoth:
                    targetPath = GetUniquePath(targetPath);
                    break;

                case ConflictResolution.Replace:
                    TryDeleteTarget(targetPath, out var deleteError);
                    if (deleteError is not null)
                        return OrganizeFileResult.Failed(file.FileName, sourcePath, deleteError);
                    break;
            }
        }

        // 执行移动
        try
        {
            File.Move(sourcePath, targetPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "文件移动失败: {Source} -> {Target}", sourcePath, targetPath);
            // 移动失败:不更新数据库
            return OrganizeFileResult.Failed(file.FileName, sourcePath, ex.Message);
        }

        // 移动成功才更新数据库
        file.AbsolutePath = targetPath;
        file.UpdatedAt = DateTime.Now;

        return resolution == ConflictResolution.KeepBoth
            ? OrganizeFileResult.Renamed(file.FileName, sourcePath, targetPath)
            : OrganizeFileResult.Moved(file.FileName, sourcePath, targetPath);
    }

    private static void TryDeleteTarget(string path, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            error = $"无法替换目标文件: {ex.Message}";
            Log.Error(ex, "替换冲突文件失败: {Path}", path);
        }
    }

    private static string GetUniquePath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            index++;
        } while (File.Exists(candidate));
        return candidate;
    }
}
