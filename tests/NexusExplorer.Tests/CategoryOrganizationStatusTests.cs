using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public sealed class CategoryOrganizationStatusTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();
    private async Task<bool> Organized(int id) => (await _host.Categories.GetByIdAsync(id))!.IsOrganized;

    [Fact]
    public async Task UnknownByDefaultAndSuccessfulRecursiveOrganizationPersistsAcrossContexts()
    {
        var parent = await _host.Categories.CreateAsync("A", null);
        var child = await _host.Categories.CreateAsync("Child", parent.Id);
        var unrelated = await _host.Categories.CreateAsync("B", null);
        Assert.False(await Organized(parent.Id)); Assert.False(await Organized(child.Id));
        var local = Path.Combine(parent.PhysicalPath, "local.txt"); File.WriteAllText(local, "LOCAL");
        await _host.Files.AddAsync(local, parent.Id);
        await _host.Files.AddAsync(_host.CreateTestFile("external.txt"), child.Id);
        Assert.False(await Organized(parent.Id)); // Even an already-local binding needs confirmation.
        Assert.All(await _host.Organization.OrganizeAsync(parent.Id), r => Assert.True(r.Success));
        Assert.True(await Organized(parent.Id)); Assert.True(await Organized(child.Id));
        Assert.False(await Organized(unrelated.Id));
        var fresh = new CategoryService(_host.DbFactory);
        Assert.True((await fresh.GetByIdAsync(parent.Id))!.IsOrganized);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("add-existing")]
    [InlineData("recategorize")]
    [InlineData("remove")]
    [InlineData("delete")]
    [InlineData("relocate-file")]
    [InlineData("rename")]
    [InlineData("move")]
    [InlineData("relocate-category")]
    [InlineData("migrate")]
    [InlineData("create-child")]
    public async Task MutationsInvalidateAffectedCategories(string operation)
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var file = await _host.Files.AddAsync(_host.CreateTestFile("file.txt"), a.Id);
        await _host.Organization.OrganizeAsync(a.Id); await _host.Organization.OrganizeAsync(b.Id);
        Assert.True(await Organized(a.Id)); Assert.True(await Organized(b.Id));
        switch (operation)
        {
            case "add":
                var local = Path.Combine(a.PhysicalPath, "new.txt"); File.WriteAllText(local, "NEW");
                await _host.Files.AddAsync(local, a.Id); break;
            case "add-existing": await _host.Files.AddAsync((await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath, b.Id); break;
            case "recategorize": await _host.Files.RecategorizeAsync(file.Id, b.Id); break;
            case "remove": await _host.Files.RemoveAsync(file.Id); break;
            case "delete": await _host.Files.DeleteAsync(file.Id, _host.RecycleBin); break;
            case "relocate-file": await _host.Files.RelocateAsync(file.Id, _host.CreateTestFile("relocated.txt")); break;
            case "rename": await _host.Categories.RenameAsync(a.Id, "Renamed"); break;
            case "move": await _host.Categories.MoveAsync(a.Id, b.Id); break;
            case "relocate-category":
                var target = Path.Combine(_host.RootDir, "relocated"); Directory.CreateDirectory(target);
                File.Copy(Path.Combine(a.PhysicalPath, "file.txt"), Path.Combine(target, "file.txt"));
                await _host.Categories.RelocateAsync(a.Id, target); break;
            case "migrate":
                var destination = Path.Combine(_host.RootDir, "destination"); Directory.CreateDirectory(destination);
                await _host.Categories.MigrateDirectoryAsync(a.Id, destination); break;
            case "create-child": await _host.Categories.CreateAsync("Child", a.Id); break;
        }
        Assert.False(await Organized(a.Id));
        Assert.Equal(operation is not ("add-existing" or "recategorize" or "move"), await Organized(b.Id));
    }

    [Fact]
    public async Task PinAndOrderingAndReadQueriesPreserveConfirmation()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        await _host.Organization.OrganizeAsync(a.Id); await _host.Organization.OrganizeAsync(b.Id);
        await _host.Categories.PinAsync(a.Id); await _host.Categories.PinAsync(b.Id);
        await _host.Categories.ReorderPinnedAsync(a.Id, 1); await _host.Categories.MoveWithinSiblingsAsync(a.Id, 1);
        await _host.Categories.UnpinAsync(a.Id);
        await _host.Categories.GetTreeAsync(); await _host.Files.GetByCategoryAsync(a.Id);
        Assert.True(await Organized(a.Id)); Assert.True(await Organized(b.Id));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("cancel-before")]
    [InlineData("cancel-between")]
    [InlineData("cancel-conflict")]
    [InlineData("registered-target")]
    public async Task IncompleteOrganizationNeverConfirmsSubtree(string failure)
    {
        var a = await _host.Categories.CreateAsync("A", null); var child = await _host.Categories.CreateAsync("Child", a.Id);
        var source = _host.CreateTestFile("A.txt"); await _host.Files.AddAsync(source, a.Id);
        await _host.Files.AddAsync(_host.CreateTestFile("Z.txt"), child.Id);
        await using (var db = _host.DbFactory.CreateDbContext())
            await db.Categories.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsOrganized, true));
        using var cancellation = new CancellationTokenSource(); FileStream? locked = null;
        Func<string, string, Task<ConflictResolution>>? conflict = null;
        if (failure == "missing") File.Delete(source);
        if (failure == "locked") locked = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None);
        if (failure == "cancel-before") cancellation.Cancel();
        if (failure is "cancel-conflict" or "registered-target")
        {
            var target = Path.Combine(a.PhysicalPath, "A.txt"); File.WriteAllText(target, "EXISTING");
            if (failure == "registered-target")
            {
                var b = await _host.Categories.CreateAsync("B", null); await _host.Files.AddAsync(target, b.Id);
            }
            conflict = (_, _) => Task.FromResult(failure == "cancel-conflict" ? ConflictResolution.Ask : ConflictResolution.Skip);
        }
        try
        {
            await _host.Organization.OrganizeAsync(a.Id, conflict, cancellation.Token,
                failure == "cancel-between" ? new InlineProgress(_ => cancellation.Cancel()) : null);
        }
        finally { locked?.Dispose(); }
        Assert.False(await Organized(a.Id)); Assert.False(await Organized(child.Id));
    }
    private sealed class InlineProgress(Action<OrganizeProgress> report) : IProgress<OrganizeProgress>
    { public void Report(OrganizeProgress value) => report(value); }

    [Fact]
    public async Task SkipUnregisteredTargetCountsAsSuccessfulConfirmation()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("A.txt", "SOURCE"); var file = await _host.Files.AddAsync(source, a.Id);
        var target = Path.Combine(a.PhysicalPath, "A.txt"); File.WriteAllText(target, "EXISTING");
        var result = await _host.Organization.OrganizeAsync(a.Id, (_, _) => Task.FromResult(ConflictResolution.Skip));
        Assert.Equal(OrganizeOutcome.Skipped, Assert.Single(result).Outcome);
        Assert.True(await Organized(a.Id)); Assert.Equal(target, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Equal("SOURCE", File.ReadAllText(source));
    }

    [Fact]
    public async Task FailedDatabaseMutationRollsBackInvalidationWithBinding()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var file = await _host.Files.AddAsync(_host.CreateTestFile("A.txt"), a.Id);
        await _host.Organization.OrganizeAsync(a.Id); await _host.Organization.OrganizeAsync(b.Id);
        await using (var db = _host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailBinding AFTER UPDATE OF CategoryId ON Files BEGIN SELECT RAISE(ABORT, 'injected failure'); END");
        await Assert.ThrowsAsync<DbUpdateException>(() => _host.Files.RecategorizeAsync(file.Id, b.Id));
        Assert.Equal(a.Id, (await _host.Files.GetByIdAsync(file.Id))!.CategoryId);
        Assert.True(await Organized(a.Id)); Assert.True(await Organized(b.Id));
    }

    [Fact]
    public async Task TreeAndStatusQueriesWorkWithoutReadingFileTable()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        await _host.Files.AddAsync(_host.CreateTestFile("A.txt"), a.Id); await _host.Organization.OrganizeAsync(a.Id);
        await using (var db = _host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Files RENAME TO UnavailableFiles");
        Assert.True(Assert.Single(await _host.Categories.GetTreeAsync()).IsOrganized);
        Assert.True((await _host.Categories.GetOrganizationStatesAsync())[a.Id]);
    }

    [Fact]
    public async Task V2UpgradePreservesPathsAndPinnedOrderAndDefaultsToUnknown()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var file = await _host.Files.AddAsync(_host.CreateTestFile("A.txt"), a.Id);
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER OrganizationFileInsert; DROP TRIGGER OrganizationFileUpdate; DROP TRIGGER OrganizationFileDelete;
                DROP TRIGGER OrganizationCategoryInsert; DROP TRIGGER OrganizationCategoryDelete; DROP TRIGGER OrganizationCategoryUpdate;
                ALTER TABLE Categories DROP COLUMN IsOrganized;
                UPDATE Categories SET IsPinned=1, PinnedOrder=91;
                PRAGMA user_version=2;
                """);
        }
        var path = Path.Combine(_host.RootDir, "test.db");
        await using (var db = _host.DbFactory.CreateDbContext()) await DatabaseInitializer.InitializeAsync(db, path);
        Assert.False(await Organized(a.Id)); Assert.Equal(91, Assert.Single(await _host.Categories.GetPinnedAsync()).PinnedOrder);
        Assert.Equal(file.AbsolutePath, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        var backup = Assert.Single(Directory.GetFiles(_host.RootDir, "test.db.before-v3-*.bak"));
        using var connection = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, command.ExecuteScalar());
        await using (var db = _host.DbFactory.CreateDbContext()) await DatabaseInitializer.InitializeAsync(db, path);
        Assert.Single(Directory.GetFiles(_host.RootDir, "test.db.before-v3-*.bak"));
        await _host.Organization.OrganizeAsync(a.Id); Assert.True(await Organized(a.Id));
        await _host.Files.RemoveAsync(file.Id); Assert.False(await Organized(a.Id));
    }

    [Fact]
    public async Task LiveTreeAndNavigationBadgesUpdateWithoutReplacingExpandedNodes()
    {
        var a = await _host.Categories.CreateAsync("A", null); await _host.Categories.PinAsync(a.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new FakePlaybackEngine());
            await main.RefreshTreeAsync(); await main.SelectCategoryAsync(await _host.Categories.GetByIdAsync(a.Id));
            var node = Assert.Single(main.Category.FlatCategories); node.IsExpanded = true;
            var panel = new CategoryFilePanel();
            var treeTemplate = (HierarchicalDataTemplate)panel.Resources[new DataTemplateKey(typeof(Category))];
            var treeContent = (StackPanel)treeTemplate.LoadContent(); treeContent.DataContext = node;
            var treeBadge = Assert.IsType<TextBlock>(treeContent.Children[2]);
            var navigation = new NavigationBar();
            var navTemplate = (DataTemplate)navigation.Resources["CategoryNavigationContent"];
            var navContent = (WrapPanel)navTemplate.LoadContent(); navContent.DataContext = Assert.Single(main.Navigation.PinnedCategories);
            var navBadge = Assert.IsType<TextBlock>(navContent.Children[1]);
            await _host.Organization.OrganizeAsync(a.Id); await main.RefreshOrganizationStatesAsync();
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal("✓ 已整理", treeBadge.Text); Assert.Equal("✓ 已整理", navBadge.Text);
            main.FileList.PickFiles = () => Task.FromResult<IReadOnlyList<string>>(new[] { _host.CreateTestFile("new.txt") });
            await main.FileList.AddFilesAsync(); await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal("↗ 待整理", treeBadge.Text); Assert.Equal("↗ 待整理", navBadge.Text);
            Assert.Same(node, Assert.Single(main.Category.FlatCategories)); Assert.True(node.IsExpanded);
            Assert.False(main.CurrentCategory!.IsOrganized);
        });
    }
}
