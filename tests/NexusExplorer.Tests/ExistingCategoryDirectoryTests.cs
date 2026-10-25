using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Tests;

public class ExistingCategoryDirectoryTests
{
    [Fact]
    public async Task ExistingDirectoryRequiresConfirmationAndBindsWithoutImportOrDiskChanges()
    {
        using var host = new TestHost();
        var parent = await host.Categories.CreateAsync("A", null);
        var path = Path.Combine(parent.PhysicalPath, "已有目录");
        Directory.CreateDirectory(Path.Combine(path, "子目录"));
        var file = Path.Combine(path, "user.txt"); File.WriteAllText(file, "USER");
        var modified = File.GetLastWriteTimeUtc(file);
        var conflict = await Assert.ThrowsAsync<ExistingCategoryDirectoryException>(() => host.Categories.CreateAsync("已有目录", parent.Id));
        Assert.Equal(path, conflict.DirectoryPath);
        Assert.Empty(await host.Categories.GetChildrenAsync(parent.Id));
        var child = await host.Categories.BindExistingDirectoryAsync("已有目录", parent.Id, conflict.DirectoryPath);
        Assert.Equal(parent.Id, child.ParentId); Assert.Equal(path, child.PhysicalPath);
        Assert.Equal("USER", File.ReadAllText(file)); Assert.Equal(modified, File.GetLastWriteTimeUtc(file));
        Assert.True(Directory.Exists(Path.Combine(path, "子目录")));
        Assert.Empty(await host.Files.GetByCategoryAsync(child.Id));
        await host.Categories.RenameAsync(child.Id, "改名");
        Assert.Equal("USER", File.ReadAllText(Path.Combine(parent.PhysicalPath, "改名", "user.txt")));
    }

    [Fact]
    public async Task DuplicateCategoryAndFileNameDoNotOfferDirectoryBinding()
    {
        using var host = new TestHost();
        await host.Categories.CreateAsync("A", null);
        var duplicate = await Assert.ThrowsAsync<OperationException>(() => host.Categories.CreateAsync("a", null));
        Assert.Equal("分类名称已存在", duplicate.Message);
        File.WriteAllText(Path.Combine(host.StorageRoot, "B"), "FILE");
        var file = await Assert.ThrowsAsync<OperationException>(() => host.Categories.CreateAsync("B", null));
        Assert.Contains("同名文件", file.Message);
    }

    [Fact]
    public async Task BindingDatabaseFailurePreservesExistingEmptyDirectory()
    {
        using var host = new TestHost();
        var path = Path.Combine(host.StorageRoot, "existing"); Directory.CreateDirectory(path);
        await using (var db = host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailCreate BEFORE INSERT ON Categories BEGIN SELECT RAISE(ABORT, 'injected create failure'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => host.Categories.BindExistingDirectoryAsync("existing", null, path));
        Assert.True(Directory.Exists(path));
        await using var check = host.DbFactory.CreateDbContext();
        Assert.Empty(await check.Categories.ToListAsync()); Assert.Empty(await check.DirectoryLocations.ToListAsync());
    }

    [Fact]
    public async Task ConfirmedBindingRevalidatesParentLocationAndDirectoryExistence()
    {
        using var host = new TestHost();
        var parent = await host.Categories.CreateAsync("A", null);
        var path = Path.Combine(parent.PhysicalPath, "child"); Directory.CreateDirectory(path);
        await host.Categories.RenameAsync(parent.Id, "B");
        var changed = await Assert.ThrowsAsync<OperationException>(() => host.Categories.BindExistingDirectoryAsync("child", parent.Id, path));
        Assert.Contains("位置已变化", changed.Message);
        var newPath = Path.Combine(host.StorageRoot, "B", "child");
        Directory.Delete(newPath);
        var missing = await Assert.ThrowsAsync<OperationException>(() => host.Categories.BindExistingDirectoryAsync("child", parent.Id, newPath));
        Assert.Contains("已不存在", missing.Message); Assert.False(Directory.Exists(newPath));
        Assert.Empty(await host.Categories.GetChildrenAsync(parent.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealCreateChildFlowHonorsConfirmation(bool accept)
    {
        using var host = new TestHost();
        var parent = await host.Categories.CreateAsync("A", null);
        var path = Path.Combine(parent.PhysicalPath, "child"); Directory.CreateDirectory(path);
        var file = Path.Combine(path, "keep.txt"); File.WriteAllText(file, "KEEP");
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            main.Category.ShowInputDialog = (_, _) => Task.FromResult<string?>("child");
            var confirmations = 0; var errors = new List<string>();
            main.Category.ShowError = errors.Add;
            main.Category.ShowConfirmDialog = message =>
            {
                confirmations++; Assert.Contains("目录已存在，是否绑定？", message); Assert.Contains(path, message);
                return Task.FromResult(accept);
            };
            await main.Category.CreateChildAsync(parent);
            Assert.Equal(1, confirmations); Assert.Empty(errors);
            Assert.Equal(accept ? 1 : 0, (await host.Categories.GetChildrenAsync(parent.Id)).Count);
            Assert.Equal("KEEP", File.ReadAllText(file));
        });
    }

    [Fact]
    public async Task CategoryCreatedDuringConfirmationIsReportedAsDuplicate()
    {
        using var host = new TestHost();
        var parent = await host.Categories.CreateAsync("A", null);
        var path = Path.Combine(parent.PhysicalPath, "child"); Directory.CreateDirectory(path);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            main.Category.ShowInputDialog = (_, _) => Task.FromResult<string?>("child");
            string? error = null; main.Category.ShowError = text => error = text;
            main.Category.ShowConfirmDialog = async _ => { await host.Categories.BindExistingDirectoryAsync("child", parent.Id, path); return true; };
            await main.Category.CreateChildAsync(parent);
            Assert.Equal("分类名称已存在", error); Assert.Single(await host.Categories.GetChildrenAsync(parent.Id));
        });
    }
}
