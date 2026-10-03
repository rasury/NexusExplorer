using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Infrastructure;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Tests;

public sealed class RefactorRegressionTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task RenameUpdatesPhysicalFilesRegardlessOfLogicalOwnership()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var child = await _host.Categories.CreateAsync("Child", a.Id);
        var b = await _host.Categories.CreateAsync("B", null);
        var path = Path.Combine(child.PhysicalPath, "clip.mp4"); File.WriteAllText(path, "original");
        var file = await _host.Files.AddAsync(path, b.Id);
        var outside = await _host.Files.AddAsync(_host.CreateTestFile("outside.mp4"), a.Id);
        await _host.Categories.RenameAsync(a.Id, "Renamed");
        var actual = await _host.Files.GetByIdAsync(file.Id);
        Assert.Equal(b.Id, actual!.CategoryId);
        Assert.Equal(Path.Combine(_host.StorageRoot, "Renamed", "Child", "clip.mp4"), actual.AbsolutePath);
        Assert.True(actual.ExistsOnDisk);
        Assert.Equal(outside.AbsolutePath, (await _host.Files.GetByIdAsync(outside.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task MoveToParentAndBackToRootPreservesFileIdentity()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var b = await _host.Categories.CreateAsync("B", null);
        var path = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(path, "original");
        var file = await _host.Files.AddAsync(path, b.Id);
        await _host.Categories.MoveAsync(a.Id, b.Id);
        Assert.Equal(Path.Combine(b.PhysicalPath, "A", "clip.mp4"), (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        await _host.Categories.MoveAsync(a.Id, null);
        Assert.Equal(path, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Null((await _host.Categories.GetByIdAsync(a.Id))!.ParentId);
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public async Task IndependentlyRelocatedChildDoesNotFollowParentRename()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var child = await _host.Categories.CreateAsync("Child", a.Id);
        var source = Path.Combine(child.PhysicalPath, "clip.mp4"); File.WriteAllText(source, "original");
        var file = await _host.Files.AddAsync(source, a.Id);
        var external = Path.Combine(_host.RootDir, "external"); Directory.Move(child.PhysicalPath, external);
        await _host.Categories.RelocateAsync(child.Id, external);
        Assert.True((await _host.Files.GetByIdAsync(file.Id))!.ExistsOnDisk);
        await _host.Categories.RenameAsync(a.Id, "Renamed");
        Assert.Equal(external, (await _host.Categories.GetByIdAsync(child.Id))!.PhysicalPath);
        Assert.Equal(Path.Combine(external, "clip.mp4"), (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task RelocateRejectsDirectoryAlreadyClaimedByAnotherCategory()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var b = await _host.Categories.CreateAsync("B", null);
        await Assert.ThrowsAsync<OperationException>(() => _host.Categories.RelocateAsync(a.Id, b.PhysicalPath));
        Assert.Equal(a.PhysicalPath, (await _host.Categories.GetByIdAsync(a.Id))!.PhysicalPath);
    }

    [Fact]
    public async Task DeleteCategoryRetainsOtherOwnedAndUnregisteredFiles()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var b = await _host.Categories.CreateAsync("B", null);
        var ownedPath = Path.Combine(a.PhysicalPath, "owned.mp4"); File.WriteAllText(ownedPath, "owned");
        var otherPath = Path.Combine(a.PhysicalPath, "other.mp4"); File.WriteAllText(otherPath, "other");
        var unmanaged = Path.Combine(a.PhysicalPath, "unmanaged.txt"); File.WriteAllText(unmanaged, "user");
        var owned = await _host.Files.AddAsync(ownedPath, a.Id);
        var other = await _host.Files.AddAsync(otherPath, b.Id);
        await _host.Categories.DeleteAsync(a.Id, _host.RecycleBin);
        Assert.Null(await _host.Files.GetByIdAsync(owned.Id));
        Assert.True((await _host.Files.GetByIdAsync(other.Id))!.ExistsOnDisk);
        Assert.True(File.Exists(unmanaged)); Assert.True(Directory.Exists(a.PhysicalPath));
        Assert.Single(_host.Categories.LastDeleteWarnings);
        // Directory identity survives the logical category removal.
        await _host.Categories.RenameAsync(b.Id, "B2");
        Assert.Equal(otherPath, (await _host.Files.GetByIdAsync(other.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task PinnedOrderIsIndependentAndPersistsInNewService()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        await _host.Categories.PinAsync(a.Id); await _host.Categories.PinAsync(b.Id);
        await _host.Categories.ReorderPinnedAsync(b.Id, 0);
        await _host.Categories.MoveWithinSiblingsAsync(b.Id, -1);
        await _host.Categories.MoveWithinSiblingsAsync(b.Id, 1);
        var fresh = new CategoryService(_host.DbFactory);
        Assert.Equal(new[] { b.Id, a.Id }, (await fresh.GetPinnedAsync()).Select(c => c.Id));
        Assert.Equal(new[] { a.Id, b.Id }, (await fresh.GetChildrenAsync(null)).Select(c => c.Id));
    }

    [Theory]
    [InlineData("Copied")]
    [InlineData("Promoted")]
    [InlineData("BeforeCommit")]
    public async Task ReplacementFailurePreservesSourceTargetAndOldMetadata(string faultStage)
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE");
        var file = await _host.Files.AddAsync(source, a.Id);
        var target = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        { Fault = stage => stage == faultStage ? Task.FromException(new IOException("injected")) : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => executor.MoveFileAsync(file.Id, target, true));
        Assert.Equal("SOURCE", File.ReadAllText(source)); Assert.Equal("TARGET", File.ReadAllText(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Empty(await executor.RecoverAsync());
        Assert.DoesNotContain(Directory.GetFiles(a.PhysicalPath), p => p.Contains(".nexus-"));
    }

    [Fact]
    public async Task CommittedMoveRecoversCleanupExactlyOnceAndBlocksNewOperations()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, a.Id);
        var target = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        { Fault = stage => stage == "Committed" ? Task.FromException(new IOException("process exit")) : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => executor.MoveFileAsync(file.Id, target, true));
        Assert.True(File.Exists(source)); Assert.Equal(target, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        await Assert.ThrowsAsync<OperationException>(() => executor.DeleteFileAsync(file.Id));
        Assert.Empty(await executor.RecoverAsync()); Assert.Empty(await executor.RecoverAsync());
        Assert.False(File.Exists(source)); Assert.Equal("SOURCE", File.ReadAllText(target));
        Assert.Single(_host.RecycleBin.RecycledFiles);
    }

    [Theory]
    [InlineData("Copied")]
    [InlineData("Promoted")]
    [InlineData("BeforeCommit")]
    public async Task DirectoryFailureRollsBackFilesAndLocation(string faultStage)
    {
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        { Fault = stage => stage == faultStage ? Task.FromException(new IOException("injected")) : Task.CompletedTask };
        var service = new CategoryService(_host.DbFactory, executor) { StorageRoot = _host.StorageRoot };
        var a = await service.CreateAsync("A", null); var path = Path.Combine(a.PhysicalPath, "user.txt"); File.WriteAllText(path, "USER");
        await Assert.ThrowsAsync<IOException>(() => service.RenameAsync(a.Id, "B"));
        Assert.Equal("USER", File.ReadAllText(path)); Assert.Equal(a.PhysicalPath, (await service.GetByIdAsync(a.Id))!.PhysicalPath);
        Assert.False(Directory.Exists(Path.Combine(_host.StorageRoot, "B")));
        Assert.Empty(await executor.RecoverAsync());
    }

    [Fact]
    public async Task UnrelatedDirectoryCreatedDuringCopyIsNeverDeletedOnRollback()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        File.WriteAllText(Path.Combine(a.PhysicalPath, "user.txt"), "USER");
        var target = Path.Combine(_host.StorageRoot, "B");
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        {
            Fault = stage =>
            {
                if (stage == "Copied") { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "user.txt"), "USER"); }
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsAnyAsync<IOException>(() => executor.ChangeDirectoryAsync(a.PhysicalPath, target, false, _ => Task.CompletedTask));
        Assert.Equal("USER", File.ReadAllText(Path.Combine(target, "user.txt")));
        Assert.Equal("USER", File.ReadAllText(Path.Combine(a.PhysicalPath, "user.txt")));
    }

    [Fact]
    public async Task CommittedDirectoryMoveRecoversWithoutLosingUnregisteredFiles()
    {
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin)
        { Fault = stage => stage == "Committed" ? Task.FromException(new IOException("process exit")) : Task.CompletedTask };
        var service = new CategoryService(_host.DbFactory, executor) { StorageRoot = _host.StorageRoot };
        var a = await service.CreateAsync("A", null); File.WriteAllText(Path.Combine(a.PhysicalPath, "user.txt"), "USER");
        await Assert.ThrowsAsync<IOException>(() => service.RenameAsync(a.Id, "B"));
        Assert.Empty(await executor.RecoverAsync());
        var updated = await service.GetByIdAsync(a.Id);
        Assert.Equal("USER", File.ReadAllText(Path.Combine(updated!.PhysicalPath, "user.txt")));
        Assert.False(Directory.Exists(a.PhysicalPath));
    }

    [Fact]
    public async Task LockedSourceReplacementNeverTouchesTarget()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var f = await _host.Files.AddAsync(source, a.Id);
        var target = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        using var locked = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None);
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin);
        await Assert.ThrowsAnyAsync<IOException>(() => executor.MoveFileAsync(f.Id, target, true));
        Assert.Equal("TARGET", File.ReadAllText(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(f.Id))!.AbsolutePath);
    }

    private sealed class InlineProgress(Action<OrganizeProgress> action) : IProgress<OrganizeProgress>
    { public void Report(OrganizeProgress value) => action(value); }

    [Fact]
    public async Task BatchCancellationKeepsCompletedMetadataAndStopsFollowingFiles()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var one = await _host.Files.AddAsync(_host.CreateTestFile("1.mp4"), a.Id);
        var two = await _host.Files.AddAsync(_host.CreateTestFile("2.mp4"), a.Id);
        using var cts = new CancellationTokenSource();
        var results = await _host.Organization.OrganizeAsync(a.Id, cancellationToken: cts.Token,
            progress: new InlineProgress(_ => cts.Cancel()));
        Assert.Single(results); Assert.True(results[0].Success);
        Assert.Equal(Path.Combine(a.PhysicalPath, "1.mp4"), (await _host.Files.GetByIdAsync(one.Id))!.AbsolutePath);
        Assert.Equal(two.AbsolutePath, (await _host.Files.GetByIdAsync(two.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task CancelConflictStopsWholeBatch()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var one = await _host.Files.AddAsync(_host.CreateTestFile("1.mp4"), a.Id);
        var two = await _host.Files.AddAsync(_host.CreateTestFile("2.mp4"), a.Id);
        File.WriteAllText(Path.Combine(a.PhysicalPath, "1.mp4"), "TARGET");
        Assert.Empty(await _host.Organization.OrganizeAsync(a.Id, (_, _) => Task.FromResult(ConflictResolution.Ask)));
        Assert.True(File.Exists(one.AbsolutePath)); Assert.True(File.Exists(two.AbsolutePath));
    }

    [Fact]
    public async Task BatchRecategorizationAdvancesOnceSkippingSuccessfulItems()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var files = new List<NexusExplorer.Models.FileItem>();
        for (var i = 1; i <= 5; i++) files.Add(await _host.Files.AddAsync(_host.CreateTestFile(i + ".mp4"), a.Id));
        var engine = new FakePlaybackEngine(); var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, engine);
        await main.SelectCategoryAsync(a); await main.SelectFileAsync(files[1]);
        await main.FileList.RecategorizeManyAsync(new[] { files[1], files[2], files[3] }, b);
        Assert.Equal(files[4].Id, main.CurrentFile!.Id);
        Assert.Equal(2, engine.Played.Count); Assert.Equal(files.Select(f => f.Id), main.Session.Queue);
    }

    [Fact]
    public async Task BrowsingAndClassifyingOtherFilesDoNotReplacePlayingQueue()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var one = await _host.Files.AddAsync(_host.CreateTestFile("1.mp4"), a.Id);
        var two = await _host.Files.AddAsync(_host.CreateTestFile("2.mp4"), a.Id);
        var engine = new FakePlaybackEngine(); var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, engine);
        await main.SelectCategoryAsync(a); await main.SelectFileAsync(one); await main.SelectCategoryAsync(b);
        await main.FileList.RecategorizeManyAsync(new[] { two }, b);
        Assert.Equal(one.Id, main.CurrentFile!.Id); Assert.Single(engine.Played);
        Assert.True(await main.PlayAdjacentAsync(1)); Assert.Equal(two.Id, main.CurrentFile!.Id);
    }

    [Fact]
    public async Task TreeRefreshPreservesExpansionAndSelectionById()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", a.Id);
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new FakePlaybackEngine());
        await main.RefreshTreeAsync(); main.Category.FlatCategories.First(c => c.Id == a.Id).IsExpanded = true;
        main.Category.FlatCategories.First(c => c.Id == b.Id).IsSelected = true;
        await _host.Categories.RenameAsync(b.Id, "Renamed"); await main.RefreshTreeAsync();
        Assert.True(main.Category.FlatCategories.First(c => c.Id == a.Id).IsExpanded);
        Assert.True(main.Category.FlatCategories.First(c => c.Id == b.Id).IsSelected);
    }

    [Fact]
    public async Task LegacyDatabaseMigrationBacksUpAndPreservesOwnershipAndIds()
    {
        var path = Path.Combine(_host.RootDir, "legacy.db"); var folder = Path.Combine(_host.RootDir, "legacy-folder"); Directory.CreateDirectory(folder);
        var filePath = Path.Combine(folder, "clip.mp4"); File.WriteAllText(filePath, "USER");
        using (var connection = new SqliteConnection("Data Source=" + path))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Categories (Id INTEGER PRIMARY KEY, ParentId INTEGER NULL, Name TEXT NOT NULL, PhysicalPath TEXT NOT NULL, SortOrder INTEGER NOT NULL, IsPinned INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                CREATE TABLE Files (Id INTEGER PRIMARY KEY, CategoryId INTEGER NOT NULL, FileName TEXT NOT NULL, AbsolutePath TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                INSERT INTO Categories VALUES (10,NULL,'A',$folder,0,1,'2020-01-01','2020-01-01');
                INSERT INTO Categories VALUES (20,NULL,'B',$other,1,0,'2020-01-01','2020-01-01');
                INSERT INTO Files VALUES (77,20,'clip.mp4',$file,'2020-01-01','2020-01-01');
                """;
            command.Parameters.AddWithValue("$folder", folder); command.Parameters.AddWithValue("$other", Path.Combine(_host.RootDir, "other"));
            command.Parameters.AddWithValue("$file", filePath); command.ExecuteNonQuery();
        }
        var factory = new DbContextFactoryStub(path);
        await using (var db = factory.CreateDbContext()) await DatabaseInitializer.InitializeAsync(db, path);
        var categories = new CategoryService(factory); var files = new FileService(factory);
        Assert.Equal(10, Assert.Single(await categories.GetPinnedAsync()).Id);
        var adopted = await files.GetByIdAsync(77); Assert.Equal(20, adopted!.CategoryId); Assert.Equal(filePath, adopted.AbsolutePath);
        Assert.NotNull(adopted.DirectoryLocationId);
        await categories.RenameAsync(10, "Renamed");
        Assert.True((await files.GetByIdAsync(77))!.ExistsOnDisk);
        Assert.Single(Directory.GetFiles(_host.RootDir, "legacy.db.before-v2-*.bak"));
        await using (var db = factory.CreateDbContext()) await DatabaseInitializer.InitializeAsync(db, path);
        Assert.Single(Directory.GetFiles(_host.RootDir, "legacy.db.before-v2-*.bak"));
    }

    [Fact]
    public async Task UnknownDatabaseStopsUpgradeWithoutCreatingReplacementTables()
    {
        var path = Path.Combine(_host.RootDir, "unknown.db");
        using var connection = new SqliteConnection("Data Source=" + path); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE UserData (Value TEXT); INSERT INTO UserData VALUES ('USER')"; command.ExecuteNonQuery();
        await using (var db = new AppDbContext(path)) await Assert.ThrowsAsync<InvalidDataException>(() => DatabaseInitializer.InitializeAsync(db, path));
        command.CommandText = "SELECT Value FROM UserData"; Assert.Equal("USER", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='Categories'"; Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task ActualSqliteCommitFailureCompensatesReplacement()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var file = await _host.Files.AddAsync(source, a.Id);
        var target = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        await using (var db = _host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailMove BEFORE UPDATE OF AbsolutePath ON Files BEGIN SELECT RAISE(ABORT, 'injected database failure'); END");
        var executor = new FileOperationExecutor(_host.DbFactory, _host.RecycleBin);
        await Assert.ThrowsAsync<DbUpdateException>(() => executor.MoveFileAsync(file.Id, target, true));
        Assert.Equal("SOURCE", File.ReadAllText(source)); Assert.Equal("TARGET", File.ReadAllText(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Empty(await executor.RecoverAsync());
    }

    [Fact]
    public async Task DeleteAndRecreateFolderReusesStableLocationWithoutStaleBinding()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        await _host.Categories.DeleteAsync(a.Id, _host.RecycleBin);
        var again = await _host.Categories.CreateAsync("A", null);
        var path = Path.Combine(again.PhysicalPath, "clip.mp4"); File.WriteAllText(path, "USER");
        var file = await _host.Files.AddAsync(path, again.Id);
        await _host.Categories.RenameAsync(again.Id, "B");
        Assert.True((await _host.Files.GetByIdAsync(file.Id))!.ExistsOnDisk);
    }

    [Fact]
    public async Task DeletePlayingFileReleasesRealFileHandleBeforeRecycle()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var file = await _host.Files.AddAsync(_host.CreateTestFile("clip.mp4"), a.Id);
        var engine = new FakePlaybackEngine { HoldFile = true };
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, engine);
        main.FileList.RecycleBin = _host.RecycleBin;
        await main.SelectCategoryAsync(a); await main.SelectFileAsync(file);
        await Assert.ThrowsAnyAsync<IOException>(() => Task.Run(() => File.Open(file.AbsolutePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose()));
        await main.FileList.DeleteManyAsync(new[] { file }, false);
        Assert.Single(_host.RecycleBin.RecycledFiles); Assert.Null(main.CurrentFile); Assert.Null(await _host.Files.GetByIdAsync(file.Id));
    }

    [Fact]
    public async Task UnicodeCaseEquivalentPathsKeepSingleFileSingleCategory()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var path = _host.CreateTestFile("Äudio.mp4");
        var one = await _host.Files.AddAsync(path, a.Id);
        var two = await _host.Files.AddAsync(Path.Combine(_host.RootDir, "äudio.mp4"), b.Id);
        Assert.Equal(one.Id, two.Id); Assert.Empty(await _host.Files.GetByCategoryAsync(a.Id));
        Assert.Single(await _host.Files.GetByCategoryAsync(b.Id));
    }
}

