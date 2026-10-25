using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.Services;
using Xunit.Abstractions;

namespace NexusExplorer.Tests;

public sealed class SameVolumeFileMoveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();
    private static FileIdentity Identity(string path) => WindowsFileIdentity.Read(path) ?? throw new IOException("测试目录不支持文件标识。");

    [Fact]
    public async Task OrganizationMoves317FilesWithoutCopyingTheirPhysicalIdentity()
    {
        var category = await _host.Categories.CreateAsync("Batch", null);
        var originals = new Dictionary<string, FileIdentity>();
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            for (var i = 0; i < 317; i++)
            {
                var source = _host.CreateTestFile($"file-{i:D3}.bin", "PAYLOAD");
                originals[Path.GetFileName(source)] = Identity(source);
                db.Files.Add(new FileItem { CategoryId = category.Id, FileName = Path.GetFileName(source), AbsolutePath = source,
                    ExternalAbsolutePath = source, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
            }
            await db.SaveChangesAsync();
        }
        var watch = Stopwatch.StartNew();
        var results = await _host.Organization.OrganizeAsync(category.Id);
        output.WriteLine($"317 个合成小文件整理：{watch.Elapsed.TotalMilliseconds:F1} ms（不代表真实磁盘或大文件批次）");
        Assert.Equal(317, results.Count); Assert.All(results, r => Assert.True(r.Success, r.Error));
        foreach (var (name, identity) in originals)
        {
            Assert.Equal(identity, Identity(Path.Combine(category.PhysicalPath, name)));
            Assert.False(File.Exists(Path.Combine(_host.RootDir, name)));
        }
        await using var check = _host.DbFactory.CreateDbContext();
        var operations = await check.FileOperations.ToListAsync();
        Assert.Equal(317, operations.Count);
        Assert.All(operations, o => { Assert.Equal("FileRename", o.Kind); Assert.Equal("Completed", o.State); Assert.Null(o.Digest); });
        Assert.True((await _host.Categories.GetByIdAsync(category.Id))!.IsOrganized);
    }

    [Theory]
    [InlineData("BeforeRename")]
    [InlineData("TargetBackedUp")]
    [InlineData("Promoted")]
    [InlineData("BeforeCommit")]
    public async Task ReplacementFailureRestoresBothPhysicalFilesAndMetadata(string fault)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE");
        var file = await _host.Files.AddAsync(source, category.Id);
        var target = Path.Combine(category.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var sourceId = Identity(source); var targetId = Identity(target);
        var operations = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        { Fault = stage => stage == fault ? Task.FromException(new IOException("injected")) : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => operations.MoveFileAsync(file.Id, target, true));
        Assert.Equal(sourceId, Identity(source)); Assert.Equal(targetId, Identity(target));
        Assert.Equal("SOURCE", File.ReadAllText(source)); Assert.Equal("TARGET", File.ReadAllText(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Empty(await operations.RecoverAsync());
        Assert.Empty(_host.RecycleBin.RecycledFiles);
        Assert.DoesNotContain(Directory.GetFiles(category.PhysicalPath), p => p.Contains(".nexus-"));
    }

    [Fact]
    public async Task ActualDatabaseCommitFailureRestoresReplacement()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, category.Id);
        var target = Path.Combine(category.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var original = Identity(source); var old = Identity(target);
        await using (var db = _host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailRename BEFORE UPDATE OF AbsolutePath ON Files BEGIN SELECT RAISE(ABORT, 'failure'); END");
        var operations = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin);
        await Assert.ThrowsAsync<DbUpdateException>(() => operations.MoveFileAsync(file.Id, target, true));
        Assert.Equal(original, Identity(source)); Assert.Equal(old, Identity(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Empty(await operations.RecoverAsync());
    }

    [Fact]
    public async Task CommittedRecoveryKeepsNewSourceAndRecyclesOldTargetOnce()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, category.Id);
        var original = Identity(source);
        var target = Path.Combine(category.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var operations = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        { Fault = stage => stage == "Committed" ? Task.FromException(new IOException("exit")) : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => operations.MoveFileAsync(file.Id, target, true));
        Assert.False(File.Exists(source)); Assert.Equal(original, Identity(target));
        Assert.Equal(target, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        await Assert.ThrowsAsync<OperationException>(() => operations.DeleteFileAsync(file.Id));
        File.WriteAllText(source, "NEW EXTERNAL FILE");
        Assert.Empty(await operations.RecoverAsync()); Assert.Empty(await operations.RecoverAsync());
        Assert.Equal("NEW EXTERNAL FILE", File.ReadAllText(source)); Assert.Equal(original, Identity(target));
        Assert.Single(_host.RecycleBin.RecycledFiles);
    }

    [Theory]
    [InlineData("Prepared")]
    [InlineData("TargetBackedUp")]
    [InlineData("Promoted")]
    public async Task InterruptedBeforeCommitRestoresSourceAndOldTarget(string interruptedAt)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, category.Id);
        var target = Path.Combine(category.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var backup = target + ".nexus-backup-test";
        var original = Identity(source); var old = Identity(target);
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            db.FileOperations.Add(new FileOperation { Kind = "FileRename", FileId = file.Id, Source = source, Target = target, Backup = backup,
                Payload = JsonSerializer.Serialize(new FileOperationExecutor.FileRenamePayload(original, old)) });
            await db.SaveChangesAsync();
        }
        if (interruptedAt != "Prepared") File.Move(target, backup);
        if (interruptedAt == "Promoted") File.Move(source, target);
        var operations = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin);
        Assert.Empty(await operations.RecoverAsync()); Assert.Empty(await operations.RecoverAsync());
        Assert.Equal(original, Identity(source)); Assert.Equal(old, Identity(target)); Assert.False(File.Exists(backup));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task RecoveryNeverMovesAnUnrelatedReplacementAtTarget()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, category.Id);
        var target = Path.Combine(category.PhysicalPath, "clip.mp4"); var preserved = target + ".external";
        var original = Identity(source);
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            db.FileOperations.Add(new FileOperation { Kind = "FileRename", FileId = file.Id, Source = source, Target = target,
                Payload = JsonSerializer.Serialize(new FileOperationExecutor.FileRenamePayload(original, null)) });
            await db.SaveChangesAsync();
        }
        File.Move(source, target); File.Move(target, preserved); File.WriteAllText(target, "UNRELATED");
        Assert.Single(await new FileOperationExecutor(_host.DbFactory).RecoverAsync());
        Assert.Equal("UNRELATED", File.ReadAllText(target)); Assert.Equal(original, Identity(preserved)); Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task DifferentVolumeDecisionStillUsesVerifiedCopy()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, category.Id);
        var original = Identity(source); var target = Path.Combine(category.PhysicalPath, "clip.mp4");
        var operations = new FileOperationExecutor(_host.DbFactory) { SameVolume = (_, _) => false };
        await operations.MoveFileAsync(file.Id, target, false);
        Assert.NotEqual(original, Identity(target)); Assert.False(File.Exists(source)); Assert.Equal("SOURCE", File.ReadAllText(target));
        await using var db = _host.DbFactory.CreateDbContext();
        var operation = Assert.Single(await db.FileOperations.ToListAsync());
        Assert.Equal("Move", operation.Kind); Assert.NotNull(operation.Digest); Assert.Equal("Completed", operation.State);
    }
}
