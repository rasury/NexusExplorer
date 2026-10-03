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

    public static OrganizeFileResult Skipped(string name, string path, string target) =>
        new() { FileName = name, OldPath = path, NewPath = target, Success = true, Outcome = OrganizeOutcome.Skipped };

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

public record OrganizeProgress(int Completed, int Total, string FileName);
public class OrganizationService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly FileOperationExecutor _operations;
    public Func<IReadOnlyCollection<string>, Task>? BeforePhysicalOperationAsync { get; set; }
    public OrganizationService(IDbContextFactory<AppDbContext> factory, FileOperationExecutor? operations = null)
    { _factory = factory; _operations = operations ?? new FileOperationExecutor(factory); }

    public Task<List<OrganizeFileResult>> OrganizeAsync(int categoryId,
        Func<string, string, Task<ConflictResolution>>? conflictHandler = null,
        CancellationToken cancellationToken = default, IProgress<OrganizeProgress>? progress = null)
    {
        var context = SynchronizationContext.Current;
        return Task.Run(() => OrganizeCoreAsync(categoryId,
            conflictHandler is null ? null : (name, path) => Infrastructure.UiDispatch.RunAsync(context, () => conflictHandler(name, path)), cancellationToken, progress));
    }
    private async Task<List<OrganizeFileResult>> OrganizeCoreAsync(int categoryId,
        Func<string, string, Task<ConflictResolution>>? conflictHandler,
        CancellationToken cancellationToken, IProgress<OrganizeProgress>? progress)
    {
        using var lease = await MutationGate.AcquireAsync(_factory);
        await using var db = await _factory.CreateDbContextAsync();
        var categories = await db.Categories.ToListAsync(); await LocationService.ResolveAsync(db, categories: categories);
        if (!categories.Any(c => c.Id == categoryId)) throw new OperationException("分类不存在。");
        var ids = new HashSet<int>(); var stack = new Stack<int>(); stack.Push(categoryId);
        while (stack.TryPop(out var id))
        { if (!ids.Add(id)) throw new OperationException("分类结构存在循环。"); foreach (var child in categories.Where(c => c.ParentId == id)) stack.Push(child.Id); }
        var files = await db.Files.Where(f => ids.Contains(f.CategoryId)).OrderBy(f => f.CategoryId).ThenBy(f => f.FileName).ThenBy(f => f.Id).ToListAsync();
        await LocationService.ResolveAsync(db, files: files);
        if (BeforePhysicalOperationAsync is not null) await BeforePhysicalOperationAsync(files.Select(f => f.AbsolutePath).ToList());
        var results = new List<OrganizeFileResult>();
        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var source = file.AbsolutePath;
            try
            {
                if (!File.Exists(source)) { results.Add(OrganizeFileResult.Missing(file.FileName, source)); continue; }
                var dir = categories.First(c => c.Id == file.CategoryId).PhysicalPath; Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, Path.GetFileName(source)); var resolution = ConflictResolution.Skip;
                if (string.Equals(LocationService.Normalize(source), LocationService.Normalize(target), StringComparison.OrdinalIgnoreCase))
                { results.Add(OrganizeFileResult.AlreadyOrganized(file.FileName, source)); continue; }
                if (File.Exists(target))
                {
                    resolution = conflictHandler is null ? ConflictResolution.Skip : await conflictHandler(file.FileName, target);
                    if (resolution == ConflictResolution.Ask) break;
                    if (resolution == ConflictResolution.Skip)
                    {
                        // Skip physical movement, but use the category's existing file.
                        await UseExistingFileAsync(file.Id, target);
                        results.Add(OrganizeFileResult.Skipped(file.FileName, source, target)); continue;
                    }
                    if (resolution == ConflictResolution.KeepBoth) target = UniquePath(target);
                }
                await _operations.MoveFileAsync(file.Id, target, resolution == ConflictResolution.Replace);
                results.Add(resolution == ConflictResolution.KeepBoth ? OrganizeFileResult.Renamed(Path.GetFileName(target), source, target) : OrganizeFileResult.Moved(file.FileName, source, target));
            }
            catch (Exception ex) { Log.Error(ex, "整理文件失败 {Path}", source); results.Add(OrganizeFileResult.Failed(file.FileName, source, ex.Message)); }
            finally { progress?.Report(new OrganizeProgress(results.Count, files.Count, file.FileName)); }
        }
        Log.Information("递归整理完成 {CategoryId}:已处理 {Count}/{Total}", categoryId, results.Count, files.Count);
        return results;
    }
    private async Task UseExistingFileAsync(int id, string target)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (await db.Files.AnyAsync(f => f.Id != id && EF.Functions.Collate(f.AbsolutePath, "NOCASE") == target))
            throw new OperationException("目标文件已有分类记录，无法跳过并改用它。请选择保留两个文件或处理已有记录。");
        if (!File.Exists(target)) throw new IOException("目标文件已不存在，原记录保留。");
        var file = await db.Files.FirstAsync(f => f.Id == id);
        file.AbsolutePath = target; file.UpdatedAt = DateTime.Now;
        await LocationService.BindFileAsync(db, file); await db.SaveChangesAsync();
    }
    private static string UniquePath(string path)
    {
        var index = 2; var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path); var extension = Path.GetExtension(path); string result;
        do { result = Path.Combine(directory, $"{stem} ({index++}){extension}"); } while (File.Exists(result));
        return result;
    }
}
