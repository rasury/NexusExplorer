using System.IO;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class CategoryRemovalTests
{
    [Fact]
    public async Task Remove_PreservesPhysicalSubtreeExternalFilesAndOtherCategoryLocations()
    {
        using var host = new TestHost();
        var root = await host.Categories.CreateAsync("移除", null);
        var child = await host.Categories.CreateAsync("子分类", root.Id);
        var grand = await host.Categories.CreateAsync("空孙分类", child.Id);
        var other = await host.Categories.CreateAsync("保留", null);
        var localPath = Path.Combine(child.PhysicalPath, "local.wav");
        var otherPath = Path.Combine(root.PhysicalPath, "other.wav");
        var unknownPath = Path.Combine(grand.PhysicalPath, "未登记.txt");
        File.WriteAllText(localPath, "local"); File.WriteAllText(otherPath, "other"); File.WriteAllText(unknownPath, "unknown");
        var externalPath = host.CreateTestFile("external.wav", "external");
        var local = await host.Files.AddAsync(localPath, child.Id);
        var external = await host.Files.AddAsync(externalPath, root.Id);
        var kept = await host.Files.AddAsync(otherPath, other.Id);
        var paths = new[] { localPath, externalPath, otherPath, unknownPath };
        var contents = paths.Select(File.ReadAllBytes).ToArray();
        var timestamps = paths.Select(File.GetLastWriteTimeUtc).ToArray();
        var directories = Directory.GetDirectories(root.PhysicalPath, "*", SearchOption.AllDirectories).Prepend(root.PhysicalPath).ToArray();
        var directoryTimes = directories.Select(Directory.GetLastWriteTimeUtc).ToArray();
        var physicalHookCalled = false;
        host.Categories.BeforePhysicalOperationAsync = _ => { physicalHookCalled = true; throw new InvalidOperationException("不应调用物理操作"); };
        await host.Categories.PinAsync(child.Id);
        Assert.Equal((2, 2), await host.Categories.GetRemovalSummaryAsync(root.Id));
        // A locked file must not prevent a registration-only removal.
        using (File.Open(localPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var removed = await host.Categories.RemoveAsync(root.Id);
            Assert.Equal(new[] { local.Id, external.Id }.Order(), removed.Order());
        }
        Assert.False(physicalHookCalled);
        Assert.Null(await host.Categories.GetByIdAsync(root.Id));
        Assert.Null(await host.Categories.GetByIdAsync(grand.Id));
        Assert.Null(await host.Files.GetByIdAsync(local.Id));
        Assert.Null(await host.Files.GetByIdAsync(external.Id));
        Assert.Empty(await host.Categories.GetPinnedAsync());
        Assert.Equal(otherPath, (await host.Files.GetByIdAsync(kept.Id))!.AbsolutePath);
        Assert.Equal(other.Id, Assert.Single(await host.Categories.GetTreeAsync()).Id);
        for (var i = 0; i < paths.Length; i++)
        { Assert.Equal(contents[i], File.ReadAllBytes(paths[i])); Assert.Equal(timestamps[i], File.GetLastWriteTimeUtc(paths[i])); }
        for (var i = 0; i < directories.Length; i++)
        { Assert.True(Directory.Exists(directories[i])); Assert.Equal(directoryTimes[i], Directory.GetLastWriteTimeUtc(directories[i])); }
        Assert.Empty(host.RecycleBin.RecycledFiles); Assert.Empty(host.RecycleBin.RecycledDirectories);
        await using var db = await host.DbFactory.CreateDbContextAsync();
        Assert.True(await db.DirectoryLocations.AnyAsync(l => l.Id == root.DirectoryLocationId));
        Assert.Empty(await db.FileOperations.ToListAsync());
    }

    [Fact]
    public async Task Remove_ParentDeletionFailureRollsBackFilesAndAlreadyRemovedChildren()
    {
        using var host = new TestHost();
        var root = await host.Categories.CreateAsync("根", null);
        var child = await host.Categories.CreateAsync("子", root.Id);
        var path = host.CreateTestFile("kept.wav", "unchanged");
        var file = await host.Files.AddAsync(path, child.Id);
        await using (var db = await host.DbFactory.CreateDbContextAsync())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_category_removal BEFORE DELETE ON Categories WHEN OLD.Name = '根' BEGIN SELECT RAISE(ABORT, 'injected removal failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => host.Categories.RemoveAsync(root.Id));
        Assert.NotNull(await host.Categories.GetByIdAsync(root.Id));
        Assert.NotNull(await host.Categories.GetByIdAsync(child.Id));
        Assert.Equal(child.Id, (await host.Files.GetByIdAsync(file.Id))!.CategoryId);
        Assert.Equal("unchanged", File.ReadAllText(path));
        Assert.True(Directory.Exists(child.PhysicalPath));
        Assert.Empty(host.RecycleBin.RecycledFiles); Assert.Empty(host.RecycleBin.RecycledDirectories);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remove_ViewModelHonorsCancellationAndRefreshesOnlyAffectedPlayback(bool playingRemovedFile)
    {
        using var host = new TestHost();
        var root = await host.Categories.CreateAsync("移除", null);
        var child = await host.Categories.CreateAsync("子", root.Id);
        var other = await host.Categories.CreateAsync("保留", null);
        var file = await host.Files.AddAsync(host.CreateTestFile("removed.wav"), child.Id);
        var kept = await host.Files.AddAsync(host.CreateTestFile("kept.wav"), other.Id);
        await host.Categories.PinAsync(child.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new FakePlaybackEngine { HoldFile = true };
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            await main.RefreshTreeAsync();
            await main.SelectCategoryAsync(child);
            await main.SelectFileAsync(playingRemovedFile ? file : kept);
            await main.Navigation.NavigateToAsync(child);
            var panel = new CategoryFilePanel();
            var menu = ((TreeView)panel.FindName("CategoryTree")).ContextMenu;
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "移除"));
            var errors = new List<string>(); main.Category.ShowError = errors.Add;
            main.Category.ShowConfirmDialog = message => { Assert.Equal($"确定移除分类「{root.Name}」？", message); return Task.FromResult(false); };
            await main.Category.RemoveAsync(root);
            Assert.NotNull(await host.Categories.GetByIdAsync(child.Id));
            Assert.True(engine.Snapshot.IsPlaying);
            main.Category.ShowConfirmDialog = _ => Task.FromResult(true);
            await main.Category.RemoveAsync(root);
            Assert.Empty(errors);
            Assert.Null(main.CurrentCategory); Assert.Empty(main.CurrentFiles);
            Assert.Empty(main.Navigation.PinnedCategories); Assert.Null(main.Navigation.SelectedCategory);
            Assert.Equal(other.Id, Assert.Single(main.Category.RootCategories).Id);
            if (playingRemovedFile)
            {
                Assert.Null(main.CurrentFile); Assert.False(engine.Snapshot.IsPlaying);
                Assert.False(main.Navigation.HasCurrentFile);
                using var exclusive = File.Open(file.AbsolutePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            else
            { Assert.Equal(kept.Id, main.CurrentFile!.Id); Assert.True(engine.Snapshot.IsPlaying); Assert.Equal(kept.AbsolutePath, engine.Path); }
        });
    }
}
