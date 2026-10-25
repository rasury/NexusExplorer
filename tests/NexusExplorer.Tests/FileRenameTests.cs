using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public sealed class FileRenameTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenamePreservesIdOwnershipContentAndResolvedLocation(bool internalLocation)
    {
        var directoryOwner = await _host.Categories.CreateAsync("Physical", null);
        var category = await _host.Categories.CreateAsync("Assigned", null);
        var source = Path.Combine(internalLocation ? directoryOwner.PhysicalPath : _host.RootDir, "原名.mp3");
        File.WriteAllText(source, "UNCHANGED CONTENT");
        var file = await _host.Files.AddAsync(source, category.Id);
        var identity = WindowsFileIdentity.Read(source);
        var renamed = await _host.Files.RenameAsync(file.Id, "新名.mp3");
        var target = Path.Combine(Path.GetDirectoryName(source)!, "新名.mp3");
        Assert.False(File.Exists(source)); Assert.Equal("UNCHANGED CONTENT", File.ReadAllText(target));
        Assert.Equal(identity, WindowsFileIdentity.Read(target));
        Assert.Equal(file.Id, renamed.Id); Assert.Equal(category.Id, renamed.CategoryId);
        Assert.Equal("新名.mp3", renamed.FileName); Assert.Equal(target, renamed.AbsolutePath);
        Assert.Equal(file.DirectoryLocationId, renamed.DirectoryLocationId);
        if (internalLocation) { Assert.Equal("新名.mp3", renamed.RelativePath); Assert.Null(renamed.ExternalAbsolutePath); }
        else { Assert.Null(renamed.DirectoryLocationId); Assert.Equal(target, renamed.ExternalAbsolutePath); }
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            var stored = await db.Files.SingleAsync(f => f.Id == file.Id);
            Assert.Equal(target, stored.AbsolutePath); Assert.Equal("新名.mp3", stored.FileName);
            var operation = Assert.Single(await db.FileOperations.ToListAsync());
            Assert.Equal("FileRename", operation.Kind); Assert.Equal("Completed", operation.State);
        }
        await _host.Categories.RenameAsync(directoryOwner.Id, "Physical-renamed");
        var resolved = (await _host.Files.GetByIdAsync(file.Id))!;
        Assert.True(File.Exists(resolved.AbsolutePath)); Assert.Equal(category.Id, resolved.CategoryId);
        Assert.Equal("新名.mp3", Path.GetFileName(resolved.AbsolutePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileOrDirectoryConflictDoesNotOverwriteOrStopPlayback(bool directory)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3", "SOURCE");
        var file = await _host.Files.AddAsync(source, category.Id);
        var target = Path.Combine(_host.RootDir, "existing.mp3");
        if (directory) Directory.CreateDirectory(target); else File.WriteAllText(target, "TARGET");
        var stopped = false; _host.Files.BeforePhysicalOperationAsync = _ => { stopped = true; return Task.CompletedTask; };
        await Assert.ThrowsAsync<OperationException>(() => _host.Files.RenameAsync(file.Id, "existing.mp3"));
        Assert.False(stopped); Assert.Equal("SOURCE", File.ReadAllText(source));
        if (directory) Assert.True(Directory.Exists(target)); else Assert.Equal("TARGET", File.ReadAllText(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        await using var db = _host.DbFactory.CreateDbContext(); Assert.Empty(await db.FileOperations.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("..")]
    [InlineData("../outside.mp3")]
    [InlineData("bad?.mp3")]
    [InlineData("bad.mp3.")]
    [InlineData("bad.mp3 ")]
    [InlineData("CON.mp3")]
    [InlineData("COM¹.wav")]
    public async Task InvalidNamesLeaveDiskAndDatabaseUnchanged(string name)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        await Assert.ThrowsAsync<OperationException>(() => _host.Files.RenameAsync(file.Id, name));
        Assert.True(File.Exists(source)); Assert.Equal("old.mp3", (await _host.Files.GetByIdAsync(file.Id))!.FileName);
        await using var db = _host.DbFactory.CreateDbContext(); Assert.Empty(await db.FileOperations.ToListAsync());
    }

    [Fact]
    public async Task AlreadyRegisteredMissingTargetIsStillAConflict()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        var target = _host.CreateTestFile("registered.mp3"); var other = await _host.Files.AddAsync(target, category.Id);
        File.Delete(target);
        await Assert.ThrowsAsync<OperationException>(() => _host.Files.RenameAsync(file.Id, "registered.mp3"));
        Assert.True(File.Exists(source)); Assert.Equal(target, (await _host.Files.GetByIdAsync(other.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task CaseOnlyRenameChangesDiskSpellingAndDatabaseWithoutCopying()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        var identity = WindowsFileIdentity.Read(source);
        var renamed = await _host.Files.RenameAsync(file.Id, "CLIP.MP3");
        Assert.Equal("CLIP.MP3", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(_host.RootDir, "*.mp3"))));
        Assert.Equal("CLIP.MP3", renamed.FileName); Assert.Equal(identity, WindowsFileIdentity.Read(renamed.AbsolutePath));
        await using var db = _host.DbFactory.CreateDbContext();
        var operation = Assert.Single(await db.FileOperations.ToListAsync()); Assert.Equal("Completed", operation.State);
        Assert.True(JsonSerializer.Deserialize<FileOperationExecutor.FileRenamePayload>(operation.Payload!)!.CaseOnly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseFailureRestoresOriginalFilenameAndRecord(bool caseOnly)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp3", "ORIGINAL"); var file = await _host.Files.AddAsync(source, category.Id);
        var identity = WindowsFileIdentity.Read(source);
        await using (var db = _host.DbFactory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailRename BEFORE UPDATE OF AbsolutePath ON Files BEGIN SELECT RAISE(ABORT, 'injected'); END");
        await Assert.ThrowsAsync<OperationException>(() => _host.Files.RenameAsync(file.Id, caseOnly ? "CLIP.MP3" : "renamed.mp3"));
        Assert.Equal("clip.mp3", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(_host.RootDir, "*.mp3"))));
        Assert.Equal("ORIGINAL", File.ReadAllText(source)); Assert.Equal(identity, WindowsFileIdentity.Read(source));
        var stored = (await _host.Files.GetByIdAsync(file.Id))!;
        Assert.Equal(source, stored.AbsolutePath); Assert.Equal("clip.mp3", stored.FileName);
        Assert.Empty(await new FileOperationExecutor(_host.DbFactory).RecoverAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupRecoveryRestoresInterruptedRenameAndReadsOlderPayload(bool caseOnly)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("clip.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        var identity = WindowsFileIdentity.Read(source)!.Value;
        var target = Path.Combine(_host.RootDir, caseOnly ? "CLIP.MP3" : "renamed.mp3");
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            db.FileOperations.Add(new FileOperation { Kind="FileRename", FileId=file.Id, Source=source, Target=target,
                Payload=caseOnly ? JsonSerializer.Serialize(new FileOperationExecutor.FileRenamePayload(identity, null, true))
                    : JsonSerializer.Serialize(new { SourceIdentity=identity, TargetIdentity=(FileIdentity?)null }) });
            await db.SaveChangesAsync();
        }
        WindowsFileIdentity.Move(source, target);
        var executor = new FileOperationExecutor(_host.DbFactory);
        Assert.Empty(await executor.RecoverAsync()); Assert.Empty(await executor.RecoverAsync());
        Assert.Equal("clip.mp3", Path.GetFileName(Assert.Single(Directory.EnumerateFiles(_host.RootDir, "*.mp3"))));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
    }

    [Fact]
    public async Task PlayingFileIsReleasedBeforeRenameAndListAndQueueKeepStableIds()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        using var engine = new FakePlaybackEngine { HoldFile=true };
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, engine);
        await main.SelectCategoryAsync(category); await main.SelectFileAsync(file);
        var queue = main.Session.Queue.ToArray(); string? error = null;
        main.FileList.ShowError = message => error=message;
        main.FileList.ShowRenameFileDialog = name => { Assert.Equal("old.mp3", name); return Task.FromResult<string?>("new.mp3"); };
        await main.FileList.RenameFileAsync(file);
        Assert.Null(error); Assert.Null(engine.Path); Assert.Null(main.CurrentFile);
        Assert.False(File.Exists(source)); Assert.True(File.Exists(Path.Combine(_host.RootDir,"new.mp3")));
        Assert.Equal(queue, main.Session.Queue); Assert.Equal(file.Id, Assert.Single(main.CurrentFiles).Id);
        Assert.Equal("new.mp3", Assert.Single(main.CurrentFiles).FileName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TargetCreatedDuringOperationIsNeverOverwritten(bool directRename)
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3", "SOURCE"); var file = await _host.Files.AddAsync(source, category.Id);
        var target = Path.Combine(_host.RootDir, "new.mp3");
        var operations = new FileOperationExecutor(_host.DbFactory)
        {
            SameVolume = (_, _) => directRename,
            Fault = stage =>
            {
                if (stage == (directRename ? "BeforeRename" : "Copied")) File.WriteAllText(target, "EXTERNAL FILE");
                return Task.CompletedTask;
            }
        };
        await Assert.ThrowsAsync<IOException>(() => operations.MoveFileAsync(file.Id, target, false));
        Assert.Equal("SOURCE", File.ReadAllText(source)); Assert.Equal("EXTERNAL FILE", File.ReadAllText(target));
        Assert.Equal(source, (await _host.Files.GetByIdAsync(file.Id))!.AbsolutePath);
        Assert.Empty(await operations.RecoverAsync());
    }

    [Fact]
    public async Task CancellingOrInvalidInputIsReportedWithoutChangingFile()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        using var engine = new FakePlaybackEngine();
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, engine);
        await main.SelectCategoryAsync(category); var errors = new List<string>(); main.FileList.ShowError = errors.Add;
        main.FileList.ShowRenameFileDialog = _ => Task.FromResult<string?>(null);
        await main.FileList.RenameFileAsync(file); Assert.Empty(errors);
        main.FileList.ShowRenameFileDialog = _ => Task.FromResult<string?>("");
        await main.FileList.RenameFileAsync(file); Assert.Single(errors);
        Assert.True(File.Exists(source)); Assert.Equal("old.mp3", Assert.Single(main.CurrentFiles).FileName);
    }

    [Fact]
    public async Task ActualContextMenuClickRenamesTheSingleSelectedFile()
    {
        var category = await _host.Categories.CreateAsync("A", null);
        var source = _host.CreateTestFile("old.mp3"); var file = await _host.Files.AddAsync(source, category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new FakePlaybackEngine());
            var panel = new CategoryFilePanel(); panel.Initialize(main, _host.RecycleBin);
            await main.SelectCategoryAsync(category);
            main.FileList.ShowRenameFileDialog = _ => Task.FromResult<string?>("from-menu.mp3");
            var list=(ListBox)panel.FindName("FileListBox");list.SelectedItem=main.CurrentFiles.Single();
            var menu=(MenuItem)panel.FindName("RenameFileMenu"); Assert.Equal("重命名…", menu.Header);
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await BoundedDialogTests.Until(()=>main.CurrentFiles.Single().FileName=="from-menu.mp3");
            Assert.Equal(file.Id, ((FileItem)list.SelectedItem).Id);
            Assert.True(File.Exists(Path.Combine(_host.RootDir,"from-menu.mp3")));
        });
    }
}
