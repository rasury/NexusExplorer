using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using NexusExplorer.Infrastructure;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>Journaled same-volume rename or verified copy, with metadata commit and recovery.</summary>
public sealed class FileOperationExecutor
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IRecycleBinService _recycle;
    internal Func<string, Task>? Fault { get; set; }
    internal Func<string, string, bool> SameVolume { get; set; } = WindowsVolume.SameVolume;
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
        var timing = Stopwatch.StartNew();
        if (SameVolume(source, target))
        {
            var identity = WindowsFileIdentity.Read(source);
            var targetIdentity = File.Exists(target) ? WindowsFileIdentity.Read(target) : (FileIdentity?)null;
            if (identity is { } original && (!File.Exists(target) || targetIdentity is not null))
            {
                if (targetIdentity == original) throw new OperationException("源文件与目标指向同一物理文件，请保留原记录。");
                Log.Information("整理文件移动开始;文件 {FileId};方式同卷直接移动;大小 {Bytes} 字节", fileId, new FileInfo(source).Length);
                await RenameFileAsync(db, file, target, original, targetIdentity);
                Log.Information("整理文件移动完成;文件 {FileId};方式同卷直接移动;耗时 {ElapsedMs:F1} ms", fileId, timing.Elapsed.TotalMilliseconds);
                return;
            }
        }
        Log.Information("整理文件移动开始;文件 {FileId};方式复制校验;大小 {Bytes} 字节", fileId, new FileInfo(source).Length);
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
            Log.Information("整理文件移动完成;文件 {FileId};方式复制校验;耗时 {ElapsedMs:F1} ms", fileId, timing.Elapsed.TotalMilliseconds);
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

    internal sealed record FileRenamePayload(FileIdentity SourceIdentity, FileIdentity? TargetIdentity);
    private async Task RenameFileAsync(AppDbContext db, FileItem file, string target, FileIdentity identity, FileIdentity? targetIdentity)
    {
        var operation = new FileOperation
        {
            Kind = "FileRename", FileId = file.Id, Source = file.AbsolutePath, Target = target,
            Backup = targetIdentity is null ? null : target + ".nexus-backup-" + Guid.NewGuid().ToString("N"),
            Payload = JsonSerializer.Serialize(new FileRenamePayload(identity, targetIdentity))
        };
        var step = Stopwatch.StartNew();
        db.FileOperations.Add(operation); await db.SaveChangesAsync();
        var journalMs = step.Elapsed.TotalMilliseconds;
        try
        {
            step.Restart();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await ProbeAsync("BeforeRename");
            if (targetIdentity is { } existing)
            {
                if (!WindowsFileIdentity.Matches(target, existing)) throw new IOException("目标文件已改变，停止替换。");
                RejectLink(target); WindowsFileIdentity.Move(target, operation.Backup!);
                await ProbeAsync("TargetBackedUp");
            }
            if (!WindowsFileIdentity.Matches(operation.Source, identity)) throw new IOException("源文件已改变，停止移动。");
            RejectLink(operation.Source); WindowsFileIdentity.Move(operation.Source, target);
            await ProbeAsync("Promoted");
            var renameMs = step.Elapsed.TotalMilliseconds; step.Restart();
            file.AbsolutePath = target; file.FileName = Path.GetFileName(target); file.UpdatedAt = DateTime.Now;
            await LocationService.BindFileAsync(db, file);
            operation.State = "Committed";
            await ProbeAsync("BeforeCommit"); await db.SaveChangesAsync();
            var commitMs = step.Elapsed.TotalMilliseconds; step.Restart();
            await ProbeAsync("Committed");
            CleanupRenamedFile(operation); await db.SaveChangesAsync();
            Log.Debug("同卷移动阶段;文件 {FileId};日志登记 {JournalMs:F1} ms;物理移动 {RenameMs:F1} ms;位置与提交 {CommitMs:F1} ms;收尾 {CleanupMs:F1} ms",
                file.Id, journalMs, renameMs, commitMs, step.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            // Reload persisted state, including a possibly successful metadata commit.
            // Never accidentally save the mutated file path during compensation.
            db.ChangeTracker.Clear(); operation = await db.FileOperations.FirstAsync(o => o.Id == operation.Id);
            if (operation.State != "Committed")
            {
                try { RestoreRenamedFile(operation); operation.State = "Failed"; }
                catch (Exception rollback) { operation.State = "RecoveryRequired"; Log.Error(rollback, "同卷文件移动补偿失败;操作 {OperationId}", operation.Id); }
            }
            operation.Error = ex.Message; await db.SaveChangesAsync(); throw;
        }
    }
    private static FileRenamePayload RenamePayload(FileOperation operation) =>
        JsonSerializer.Deserialize<FileRenamePayload>(operation.Payload ?? throw new InvalidDataException("文件移动日志缺少标识。"))
        ?? throw new InvalidDataException("文件移动日志标识无效。");
    private static void RestoreRenamedFile(FileOperation operation)
    {
        var payload = RenamePayload(operation);
        if (File.Exists(operation.Source))
        {
            if (!WindowsFileIdentity.Matches(operation.Source, payload.SourceIdentity)) throw new IOException("源路径被其他文件占用，保留现场。");
            if (WindowsFileIdentity.Matches(operation.Target, payload.SourceIdentity)) throw new IOException("源和目标均包含原文件，保留现场。");
        }
        else
        {
            if (!WindowsFileIdentity.Matches(operation.Target, payload.SourceIdentity)) throw new IOException("移动后的文件标识不符，保留现场。");
            WindowsFileIdentity.Move(operation.Target, operation.Source);
        }
        if (operation.Backup is not null && File.Exists(operation.Backup))
        {
            if (payload.TargetIdentity is not { } old || !WindowsFileIdentity.Matches(operation.Backup, old)) throw new IOException("旧目标备份标识不符，保留现场。");
            if (File.Exists(operation.Target)) throw new IOException("目标路径被占用，保留原文件与备份。");
            WindowsFileIdentity.Move(operation.Backup, operation.Target);
        }
        else if (payload.TargetIdentity is { } old && !WindowsFileIdentity.Matches(operation.Target, old))
            throw new IOException("旧目标无法确认，保留现场等待恢复。");
    }
    private void CleanupRenamedFile(FileOperation operation)
    {
        var payload = RenamePayload(operation);
        if (!WindowsFileIdentity.Matches(operation.Target, payload.SourceIdentity)) throw new IOException("已提交文件标识不符，保留备份等待恢复。");
        // Source was renamed, not copied. Never delete anything recreated there.
        if (operation.Backup is not null && File.Exists(operation.Backup))
        {
            if (payload.TargetIdentity is not { } old || !WindowsFileIdentity.Matches(operation.Backup, old)) throw new IOException("旧目标备份标识不符，停止清理。");
            if (!_recycle.SendFileToRecycleBin(operation.Backup)) throw new IOException("旧目标已保留在备份位置，回收站清理失败。");
        }
        operation.State = "Completed"; operation.Error = null;
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

    public async Task ChangeDirectoryAsync(string source, string target, bool relocateOnly, Func<AppDbContext, Task> commit, bool preferRename = false)
    {
        source = LocationService.Normalize(source); target = LocationService.Normalize(target);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new OperationException("目标与当前目录相同。");
        if (LocationService.IsWithin(target, source) || LocationService.IsWithin(source, target))
            throw new OperationException("源目录与目标目录不能相互包含。");
        if (relocateOnly && !Directory.Exists(target)) throw new OperationException("目标目录不存在。");
        if (!relocateOnly && (!Directory.Exists(source) || Directory.Exists(target)))
            throw new OperationException("源目录不存在或目标目录已存在。");
        if (!relocateOnly && preferRename && Infrastructure.WindowsVolume.SameVolume(source, target))
        { await RenameDirectoryAsync(source, target, commit); return; }
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
    private async Task RenameDirectoryAsync(string source, string target, Func<AppDbContext, Task> commit)
    {
        // Enumerate links, never read/hash/copy file contents on this same-volume path.
        var pending = new Stack<string>(); pending.Push(source);
        while (pending.TryPop(out var directory))
        {
            RejectLink(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            { RejectLink(entry); if (Directory.Exists(entry)) pending.Push(entry); }
        }
        await using var db = await _factory.CreateDbContextAsync(); await EnsureReadyAsync(db);
        var operation = new FileOperation { Kind = "DirectoryRename", Source = source, Target = target };
        db.FileOperations.Add(operation); await db.SaveChangesAsync();
        var committed = false;
        var renamed = false;
        try
        {
            operation.State = "Promoting"; await db.SaveChangesAsync();
            await ProbeAsync("BeforeRename");
            Directory.Move(source, target); renamed = true;
            await ProbeAsync("Promoted");
            await commit(db); operation.State = "Committed";
            await ProbeAsync("BeforeCommit"); await db.SaveChangesAsync(); committed = true;
            await ProbeAsync("Committed");
            operation.State = "Completed"; await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear(); operation = await db.FileOperations.FirstAsync(o => o.Id == operation.Id);
            if (!committed)
            {
                try { if (renamed) RestoreRenamedDirectory(operation); operation.State = "Failed"; }
                catch (Exception rollback) { operation.State = "RecoveryRequired"; Log.Error(rollback, "目录直接移动补偿失败"); }
            }
            operation.Error = ex.Message; await db.SaveChangesAsync(); throw;
        }
    }
    private static void RestoreRenamedDirectory(FileOperation operation)
    {
        if (Directory.Exists(operation.Source))
        {
            if (Directory.Exists(operation.Target)) throw new IOException("源和目标目录均存在，保留现场等待检查。");
            return;
        }
        if (!Directory.Exists(operation.Target)) throw new IOException("源和目标目录均不存在，无法恢复。");
        Directory.Move(operation.Target, operation.Source);
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
                else if (operation.Kind == "FileRename")
                {
                    if (operation.State == "Committed") CleanupRenamedFile(operation);
                    else { RestoreRenamedFile(operation); operation.State = "Failed"; }
                }
                else if (operation.Kind == "DirectoryRename")
                {
                    if (operation.State == "Committed")
                    {
                        if (!Directory.Exists(operation.Target)) throw new IOException("已提交目录位置缺失，保留现场。");
                        operation.State = "Completed";
                    }
                    else
                    {
                        RestoreRenamedDirectory(operation); operation.State = "Failed";
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
