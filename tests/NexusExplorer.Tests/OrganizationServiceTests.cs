using System.IO;
using NexusExplorer.Services;

namespace NexusExplorer.Tests;

public class OrganizationServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Organize_MovesFilesAndUpdatesDatabase()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4");
        var b = _host.CreateTestFile("B.mp4");
        var fileA = await _host.Files.AddAsync(a, scifi.Id);
        var fileB = await _host.Files.AddAsync(b, scifi.Id);

        var results = await _host.Organization.OrganizeAsync(scifi.Id);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));

        // 文件移动到分类目录
        var expectedA = Path.Combine(scifi.PhysicalPath, "A.mp4");
        Assert.True(File.Exists(expectedA));
        Assert.False(File.Exists(a));

        // 数据库路径已更新
        var updatedA = await _host.Files.GetByIdAsync(fileA.Id);
        Assert.Equal(Path.GetFullPath(expectedA), updatedA!.AbsolutePath);
        var updatedB = await _host.Files.GetByIdAsync(fileB.Id);
        Assert.Contains("B.mp4", updatedB!.AbsolutePath);
    }

    [Fact]
    public async Task Organize_CreatesTargetDirectoryIfMissing()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4");
        await _host.Files.AddAsync(a, scifi.Id);

        // 模拟物理目录被外部删除
        Directory.Delete(scifi.PhysicalPath);

        var results = await _host.Organization.OrganizeAsync(scifi.Id);

        Assert.True(results[0].Success);
        Assert.True(Directory.Exists(scifi.PhysicalPath));
        Assert.True(File.Exists(Path.Combine(scifi.PhysicalPath, "A.mp4")));
    }

    [Fact]
    public async Task Organize_DoesNotRecurseIntoSubcategories()
    {
        var parent = await _host.Categories.CreateAsync("电影", null);
        var child = await _host.Categories.CreateAsync("科幻", parent.Id);

        var fileParent = _host.CreateTestFile("P.mp4");
        var fileChild = _host.CreateTestFile("C.mp4");
        await _host.Files.AddAsync(fileParent, parent.Id);
        await _host.Files.AddAsync(fileChild, child.Id);

        // 只整理父分类
        var results = await _host.Organization.OrganizeAsync(parent.Id);

        Assert.Single(results);
        Assert.True(File.Exists(Path.Combine(parent.PhysicalPath, "P.mp4")));
        // 子分类的文件不动
        Assert.True(File.Exists(fileChild));
        Assert.Empty(Directory.GetFiles(child.PhysicalPath));
    }

    [Fact]
    public async Task Organize_MissingSourceFile_SkippedWithoutDatabaseUpdate()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4");
        var file = await _host.Files.AddAsync(a, scifi.Id);

        // 外部删除
        File.Delete(a);

        var results = await _host.Organization.OrganizeAsync(scifi.Id);

        Assert.Equal(OrganizeOutcome.Missing, results[0].Outcome);
        var updated = await _host.Files.GetByIdAsync(file.Id);
        // 数据库保留原路径(等待用户重新定位)
        Assert.Equal(Path.GetFullPath(a), updated!.AbsolutePath);
    }

    [Fact]
    public async Task Organize_ConflictSkip_KeepsBothButSourceStays()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4", "source-content");
        await _host.Files.AddAsync(a, scifi.Id);

        // 目标目录已有同名文件
        File.WriteAllText(Path.Combine(scifi.PhysicalPath, "A.mp4"), "target-content");

        var results = await _host.Organization.OrganizeAsync(
            scifi.Id,
            (_, _) => Task.FromResult(ConflictResolution.Skip));

        Assert.Equal(OrganizeOutcome.Skipped, results[0].Outcome);
        // 源文件未动,目标文件未覆盖
        Assert.Equal("source-content", File.ReadAllText(a));
        Assert.Equal("target-content", File.ReadAllText(Path.Combine(scifi.PhysicalPath, "A.mp4")));
    }

    [Fact]
    public async Task Organize_ConflictReplace_OverwritesTarget()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4", "source-content");
        await _host.Files.AddAsync(a, scifi.Id);

        File.WriteAllText(Path.Combine(scifi.PhysicalPath, "A.mp4"), "target-content");

        var results = await _host.Organization.OrganizeAsync(
            scifi.Id,
            (_, _) => Task.FromResult(ConflictResolution.Replace));

        Assert.Equal(OrganizeOutcome.Moved, results[0].Outcome);
        Assert.Equal("source-content", File.ReadAllText(Path.Combine(scifi.PhysicalPath, "A.mp4")));
        Assert.False(File.Exists(a));
    }

    [Fact]
    public async Task Organize_ConflictKeepBoth_RenamesAutomatically()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4", "source-content");
        await _host.Files.AddAsync(a, scifi.Id);

        File.WriteAllText(Path.Combine(scifi.PhysicalPath, "A.mp4"), "target-content");

        var results = await _host.Organization.OrganizeAsync(
            scifi.Id,
            (_, _) => Task.FromResult(ConflictResolution.KeepBoth));

        Assert.Equal(OrganizeOutcome.Renamed, results[0].Outcome);
        Assert.True(File.Exists(Path.Combine(scifi.PhysicalPath, "A (2).mp4")));
        Assert.Equal("source-content", File.ReadAllText(Path.Combine(scifi.PhysicalPath, "A (2).mp4")));
        Assert.Equal("target-content", File.ReadAllText(Path.Combine(scifi.PhysicalPath, "A.mp4")));

        // 数据库指向改名后的文件
        var file = (await _host.Files.GetByCategoryAsync(scifi.Id))[0];
        Assert.Contains("A (2).mp4", file.AbsolutePath);
    }

    [Fact]
    public async Task Organize_AlreadyInPlace_NoMoveNeeded()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4");
        await _host.Files.AddAsync(a, scifi.Id);

        // 先整理一次
        await _host.Organization.OrganizeAsync(scifi.Id);

        // 再整理一次:已在目标位置
        var results = await _host.Organization.OrganizeAsync(scifi.Id);
        Assert.Equal(OrganizeOutcome.AlreadyOrganized, results[0].Outcome);
    }

    [Fact]
    public async Task Organize_MoveFails_DatabaseNotUpdated()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var locked = _host.CreateTestFile("locked.mp4");
        var file = await _host.Files.AddAsync(locked, scifi.Id);

        // 独占锁定源文件使 File.Move 失败
        using (var lockStream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var results = await _host.Organization.OrganizeAsync(scifi.Id);

            Assert.False(results[0].Success);
            Assert.Equal(OrganizeOutcome.Failed, results[0].Outcome);
        }

        // 数据库不更新
        var updated = await _host.Files.GetByIdAsync(file.Id);
        Assert.Equal(Path.GetFullPath(locked), updated!.AbsolutePath);
        // 目标目录没有文件
        Assert.Empty(Directory.GetFiles(scifi.PhysicalPath));
    }

    [Fact]
    public async Task Organize_FileNotLocked_MovesSuccessfully()
    {
        // 回归:播放中的文件整理曾报"being used by another process"
        // (UI 层已改为整理前停止播放;服务层验证正常移动路径)
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4");
        await _host.Files.AddAsync(a, scifi.Id);

        var results = await _host.Organization.OrganizeAsync(scifi.Id);

        Assert.True(results[0].Success);
        Assert.Equal(OrganizeOutcome.Moved, results[0].Outcome);
        Assert.True(File.Exists(Path.Combine(scifi.PhysicalPath, "A.mp4")));
    }

    [Fact]
    public async Task Organize_NoConflictHandler_DefaultsToSkip()
    {
        var scifi = await _host.Categories.CreateAsync("科幻", null);
        var a = _host.CreateTestFile("A.mp4");
        await _host.Files.AddAsync(a, scifi.Id);

        File.WriteAllText(Path.Combine(scifi.PhysicalPath, "A.mp4"), "target-content");

        // 不提供回调:默认跳过,绝不误删
        var results = await _host.Organization.OrganizeAsync(scifi.Id);

        Assert.Equal(OrganizeOutcome.Skipped, results[0].Outcome);
        Assert.True(File.Exists(a));
    }
}
