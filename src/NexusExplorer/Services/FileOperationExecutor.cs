using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>Durable copy/verify/commit/cleanup protocol. Originals survive until metadata commits.</summary>
public sealed class FileOperationExecutor
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IRecycleBinService _recycle;
    internal Func<string, Task>? Fault { get; set; }
    public FileOperationExecutor(IDbContextFactory<AppDbContext> factory, IRecycleBinService? recycle = null)
    { _factory = factory; _recycle = recycle ?? new RecycleBinService(); }
    private Task ProbeAsync(string stage) => Fault?.Invoke(stage) ?? Task.CompletedTask;
    private async Task EnsureReadyAsync(AppDbContext db)
    {
        if (await db.FileOperations.AnyAsync(o => o.State != "Completed" && o.State != "Failed"))
            throw new OperationException("存在未完成的文件操作，请重新启动以恢复，恢复完成前暂停新的文件操作。日志保留了原文件与目标位置。");
    }
    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
    private static async Task<bool> MatchesAsync(string path, string? hash) => File.Exists(path) && await HashAsync(path) == hash;
    private static async Task CopyAsync(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output); await output.FlushAsync(); output.Flush(true);
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new OperationException("为避免跨目录操作，暂不搬迁符号链接或目录联接点。");
    }

    public async Task MoveFileAsync(int fileId, string target, bool replace)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await EnsureReadyAsync(db);
        var file = await db.Files.FirstAsync(f => f.Id == fileId);
        await LocationService.ResolveAsync(db, files: new[] { file });
        var source = file.AbsolutePath; target = LocationService.Normalize(target);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return;
        RejectLink(source);
        if (await db.Files.AnyAsync(f => f.Id != fileId && EF.Functions.Collate(f.AbsolutePath, "NOCASE") == target))
            throw new OperationException("目标路径已有受管理文件，请选择保留两个文件。");
        if (File.Exists(target) && !replace) throw new IOException("目标文件已存在。");
        var stage = target + ".nexus-stage-" + Guid.NewGuid().ToString("N");
        var operation = new FileOperation
        {
            FileId = fileId, Source = source, Target = target, Payload = stage,
            Backup = File.Exists(target) ? target + ".nexus-backup-" + Guid.NewGuid().ToString("N") : null,
            Digest = await HashAsync(source)
        };
        db.FileOperations.Add(operation); await db.SaveChangesAsync();
        var committed = false;
        try
        {
            await CopyAsync(source, stage);
            if (!await MatchesAsync(stage, operation.Digest)) throw new IOException("文件复制校验失败。");
            await ProbeAsync("Copied");
            operation.State = "Promoting"; await db.SaveChangesAsync();
            if (operation.Backup is not null) File.Move(target, operation.Backup);
            File.Move(stage, target);
            await ProbeAsync("Promoted");
            file.AbsolutePath = target; file.FileName = Path.GetFileName(target); file.UpdatedAt = DateTime.Now;
            await LocationService.BindFileAsync(db, file);
            operation.State = "Committed";
            await ProbeAsync("BeforeCommit");
            await db.SaveChangesAsync(); committed = true;
            await ProbeAsync("Committed");
            await CleanupFileAsync(operation);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "文件操作失败 {Source} -> {Target}", source, target);
            // Never save the mutated File entity after a failed metadata commit.
            db.Entry(file).State = EntityState.Detached;
            if (!committed)
            {
                try
                {
                    await RollbackFileAsync(operation);
                    operation.State = "Failed";
                }
                catch (Exception rollback) { operation.State = "RecoveryRequired"; Log.Error(rollback, "文件操作补偿失败"); }
            }
            operation.Error = ex.Message; await db.SaveChangesAsync(); throw;
        }
    }

    private async Task CleanupFileAsync(FileOperation operation)
    {
        if (!await MatchesAsync(operation.Target, operation.Digest)) throw new IOException("目标文件发生变化，保留原文件等待恢复。");
        if (File.Exists(operation.Source))
        {
            if (!await MatchesAsync(operation.Source, operation.Digest)) throw new IOException("源文件发生变化，保留源文件等待处理。");
            File.Delete(operation.Source);
        }
        if (operation.Backup is not null && File.Exists(operation.Backup) && !_recycle.SendFileToRecycleBin(operation.Backup))
            throw new IOException("旧目标已保留在备份位置，回收站清理失败。");
        operation.State = "Completed"; operation.Error = null;
    }
    private static async Task RollbackFileAsync(FileOperation operation)
    {
        if (!await MatchesAsync(operation.Source, operation.Digest)) throw new IOException("源文件无法验证，禁止自动回滚。");
        var promoted = operation.State is "Promoting" or "Committed" or "RecoveryRequired"
            && operation.Payload is not null && !File.Exists(operation.Payload);
        if (File.Exists(operation.Target) && promoted)
        {
            if (!await MatchesAsync(operation.Target, operation.Digest))
            {
                // Prepared operation may not have touched an existing target yet.
                throw new IOException("目标内容发生变化，禁止自动回滚。");
            }
            // A pre-existing equal-content target is still user data until backup exists.
            File.Delete(operation.Target);
        }
        if (operation.Backup is not null && File.Exists(operation.Backup))
        {
            if (File.Exists(operation.Target)) throw new IOException("目标路径被占用，保留备份等待恢复。");
            File.Move(operation.Backup, operation.Target);
        }
        if (operation.Payload is not null && File.Exists(operation.Payload)) File.Delete(operation.Payload);
    }

    public record Manifest(string RelativePath, string Digest);
    public record DirectoryPayload(string Stage, List<Manifest> Files, List<string> Directories);

    public async Task ChangeDirectoryAsync(string source, string target, bool relocateOnly, Func<AppDbContext, Task> commit)
    {
        source = LocationService.Normalize(source); target = LocationService.Normalize(target);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("目标与当前目录相同。");
        if (LocationService.IsWithin(target, source) || LocationService.IsWithin(source, target))
            throw new OperationException("源目录与目标目录不能相互包含。");
        if (relocateOnly && !Directory.Exists(target)) throw new OperationException("目标目录不存在。");
        if (!relocateOnly && (!Directory.Exists(source) || Directory.Exists(target)))
            throw new OperationException("源目录不存在或目标目录已存在。");
        var stage = target + ".nexus-stage-" + Guid.NewGuid().ToString("N");
        var manifest = new List<Manifest>(); var dirs = new List<string>();
        if (!relocateOnly)
        {
            RejectLink(source);
            void Visit(string directory)
            {
                RejectLink(directory); dirs.Add(Path.GetRelativePath(source, directory));
                foreach (var child in Directory.EnumerateDirectories(directory)) Visit(child);
            }
            Visit(source);
            foreach (var dir in dirs)
                foreach (var file in Directory.EnumerateFiles(Path.Combine(source, dir)))
                { RejectLink(file); manifest.Add(new Manifest(Path.GetRelativePath(source, file), await HashAsync(file))); }
        }
        var payload = new DirectoryPayload(stage, manifest, dirs);
        await using var db = await _factory.CreateDbContextAsync();
        await EnsureReadyAsync(db);
        var operation = new FileOperation { Kind = relocateOnly ? "Relocate" : "Directory", Source = source, Target = target, Payload = JsonSerializer.Serialize(payload) };
        db.FileOperations.Add(operation); await db.SaveChangesAsync();
        var committed = false;
        try
        {
            if (!relocateOnly)
            {
                foreach (var dir in dirs) Directory.CreateDirectory(Path.Combine(stage, dir));
                foreach (var item in manifest)
                {
                    var destination = Path.Combine(stage, item.RelativePath);
                    await CopyAsync(Path.Combine(source, item.RelativePath), destination);
                    if (!await MatchesAsync(destination, item.Digest)) throw new IOException("目录复制校验失败。");
                }
                await ProbeAsync("Copied");
                operation.State = "Promoting"; await db.SaveChangesAsync();
                Directory.Move(stage, target); await ProbeAsync("Promoted");
            }
            await commit(db); operation.State = "Committed";
            await ProbeAsync("BeforeCommit"); await db.SaveChangesAsync(); committed = true;
            await ProbeAsync("Committed");
            if (!relocateOnly) await CleanupDirectoryAsync(operation, payload);
            else operation.State = "Completed";
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            operation = await db.FileOperations.FirstAsync(o => o.Id == operation.Id);
            if (!committed)
            {
                try { await RollbackDirectoryAsync(operation, payload); operation.State = "Failed"; }
                catch (Exception rollback) { operation.State = "RecoveryRequired"; Log.Error(rollback, "目录操作补偿失败"); }
            }
            operation.Error = ex.Message; await db.SaveChangesAsync(); throw;
        }
    }
    private static async Task RemoveManifestAsync(string root, DirectoryPayload payload)
    {
        foreach (var dir in payload.Directories)
            if (Directory.Exists(Path.Combine(root, dir))) RejectLink(Path.Combine(root, dir));
        foreach (var item in payload.Files)
        {
            var path = Path.Combine(root, item.RelativePath);
            if (!File.Exists(path)) continue;
            RejectLink(path);
            if (!await MatchesAsync(path, item.Digest)) throw new IOException("目录内容变化，保留现场等待恢复。");
        }
        foreach (var item in payload.Files) if (File.Exists(Path.Combine(root, item.RelativePath))) File.Delete(Path.Combine(root, item.RelativePath));
        foreach (var dir in payload.Directories.OrderByDescending(d => d.Length))
        {
            var path = Path.Combine(root, dir);
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
        }
        if (Directory.Exists(root)) throw new IOException("目录含新增文件，已保留，需手动检查。");
    }
    private static async Task CleanupDirectoryAsync(FileOperation operation, DirectoryPayload payload)
    {
        foreach (var item in payload.Files)
            if (!await MatchesAsync(Path.Combine(operation.Target, item.RelativePath), item.Digest)) throw new IOException("目标目录验证失败，保留源目录。");
        if (Directory.Exists(operation.Source)) await RemoveManifestAsync(operation.Source, payload);
        operation.State = "Completed"; operation.Error = null;
    }
    private static async Task RollbackDirectoryAsync(FileOperation operation, DirectoryPayload payload)
    {
        if (operation.Kind == "Relocate") return;
        foreach (var item in payload.Files)
            if (!await MatchesAsync(Path.Combine(operation.Source, item.RelativePath), item.Digest)) throw new IOException("源目录验证失败，禁止自动回滚。");
        var promoted = operation.State is "Promoting" or "Committed" or "RecoveryRequired" && !Directory.Exists(payload.Stage);
        if (Directory.Exists(operation.Target) && promoted) await RemoveManifestAsync(operation.Target, payload);
        if (Directory.Exists(payload.Stage))
        {
            // A partial copy is owned by this operation and never contains user files.
            foreach (var dir in payload.Directories)
                if (Directory.Exists(Path.Combine(payload.Stage, dir))) RejectLink(Path.Combine(payload.Stage, dir));
            foreach (var item in payload.Files)
            {
                var file = Path.Combine(payload.Stage, item.RelativePath);
                if (File.Exists(file)) { RejectLink(file); File.Delete(file); }
            }
            foreach (var dir in payload.Directories.OrderByDescending(d => d.Length))
            {
                var path = Path.Combine(payload.Stage, dir);
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            if (Directory.Exists(payload.Stage)) throw new IOException("暂存目录含其他文件，保留现场等待检查。");
        }
    }

    public async Task DeleteFileAsync(int fileId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await EnsureReadyAsync(db);
        var file = await db.Files.FirstAsync(f => f.Id == fileId); await LocationService.ResolveAsync(db, files: new[] { file });
        var operation = new FileOperation { Kind = "Delete", FileId = fileId, Source = file.AbsolutePath };
        db.FileOperations.Add(operation); await db.SaveChangesAsync();
        if (File.Exists(operation.Source) && !_recycle.SendFileToRecycleBin(operation.Source))
        { operation.State = "Failed"; operation.Error = "无法送入回收站"; await db.SaveChangesAsync(); throw new OperationException("文件无法送入回收站，记录已保留。"); }
        db.Files.Remove(file); operation.State = "Completed"; await db.SaveChangesAsync();
    }

    public async Task<List<string>> RecoverAsync()
    {
        await using var db = await _factory.CreateDbContextAsync(); var problems = new List<string>();
        foreach (var operation in await db.FileOperations.Where(o => o.State != "Completed" && o.State != "Failed").OrderBy(o => o.Id).ToListAsync())
        {
            try
            {
                if (operation.Kind == "Delete")
                {
                    if (File.Exists(operation.Source)) operation.State = "Failed";
                    else
                    {
                        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == operation.FileId);
                        if (file is not null) db.Files.Remove(file); operation.State = "Completed";
                    }
                }
                else if (operation.Kind is "Directory" or "Relocate")
                {
                    var payload = JsonSerializer.Deserialize<DirectoryPayload>(operation.Payload!)!;
                    if (operation.State == "Committed")
                    { if (operation.Kind == "Directory") await CleanupDirectoryAsync(operation, payload); else operation.State = "Completed"; }
                    else { await RollbackDirectoryAsync(operation, payload); operation.State = "Failed"; }
                }
                else if (operation.Kind == "Move")
                {
                    if (operation.State == "Committed") await CleanupFileAsync(operation);
                    else { await RollbackFileAsync(operation); operation.State = "Failed"; }
                }
                else throw new InvalidDataException("无法识别操作日志，保留现场等待检查。");
                await db.SaveChangesAsync();
            }
            catch (Exception ex) { operation.Error = ex.Message; problems.Add($"{operation.Source}: {ex.Message}"); await db.SaveChangesAsync(); }
        }
        return problems;
    }
}
