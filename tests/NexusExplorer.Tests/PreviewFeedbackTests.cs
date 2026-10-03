using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using NexusExplorer.Data;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public sealed class PreviewFeedbackTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task SameVolumeRenamePreservesNativeFileIdentityInsteadOfCopying()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var path = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(path, "contents");
        var file = await _host.Files.AddAsync(path, b.Id);
        var identity = FileIdentity(path);
        await _host.Categories.RenameAsync(a.Id, "renamed");
        await _host.Categories.MoveAsync(a.Id, b.Id);
        var updated = await _host.Files.GetByIdAsync(file.Id);
        Assert.Equal(Path.Combine(b.PhysicalPath, "renamed", "clip.mp4"), updated!.AbsolutePath);
        Assert.Equal(b.Id, updated.CategoryId); Assert.True(updated.ExistsOnDisk);
        Assert.Equal(identity, FileIdentity(updated.AbsolutePath));
        await using var db = _host.DbFactory.CreateDbContext();
        Assert.All(await db.FileOperations.ToListAsync(), o => { Assert.Equal("DirectoryRename", o.Kind); Assert.Equal("Completed", o.State); });
    }
    private static (uint Volume, uint High, uint Low) FileIdentity(string path)
    {
        using var file = File.OpenRead(path);
        Assert.True(GetFileInformationByHandle(file.SafeFileHandle, out var info));
        return (info.Volume, info.IndexHigh, info.IndexLow);
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll")] private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out FileInformation info);

    [Theory]
    [InlineData("Promoted")]
    [InlineData("BeforeCommit")]
    public async Task DirectRenameFailureRestoresDirectoryAndMetadata(string stage)
    {
        var operations = new FileOperationExecutor(_host.DbFactory) { Fault = s => s == stage ? Task.FromException(new IOException("fault")) : Task.CompletedTask };
        var categories = new CategoryService(_host.DbFactory, operations) { StorageRoot = _host.StorageRoot };
        var a = await categories.CreateAsync("A", null); File.WriteAllText(Path.Combine(a.PhysicalPath, "unregistered.txt"), "KEEP");
        await Assert.ThrowsAsync<IOException>(() => categories.RenameAsync(a.Id, "B"));
        Assert.Equal(a.PhysicalPath, (await categories.GetByIdAsync(a.Id))!.PhysicalPath);
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(a.PhysicalPath, "unregistered.txt")));
        Assert.False(Directory.Exists(Path.Combine(_host.StorageRoot, "B")));
    }

    [Fact]
    public async Task InterruptedDirectRenameRecoversUncommittedLocationAndIsIdempotent()
    {
        var a = await _host.Categories.CreateAsync("A", null); var target = Path.Combine(_host.StorageRoot, "B");
        File.WriteAllText(Path.Combine(a.PhysicalPath, "unregistered.txt"), "KEEP");
        await using (var db = _host.DbFactory.CreateDbContext())
        {
            db.FileOperations.Add(new NexusExplorer.Models.FileOperation { Kind = "DirectoryRename", Source = a.PhysicalPath, Target = target, State = "Promoting" });
            await db.SaveChangesAsync();
        }
        Directory.Move(a.PhysicalPath, target);
        var operations = new FileOperationExecutor(_host.DbFactory);
        Assert.Empty(await operations.RecoverAsync()); Assert.Empty(await operations.RecoverAsync());
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(a.PhysicalPath, "unregistered.txt")));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task SkipConflictWithRegisteredTargetKeepsBothRecordsAndFiles()
    {
        var a = await _host.Categories.CreateAsync("A", null); var b = await _host.Categories.CreateAsync("B", null);
        var source = _host.CreateTestFile("clip.mp4", "SOURCE"); var external = await _host.Files.AddAsync(source, a.Id);
        var target = Path.Combine(a.PhysicalPath, "clip.mp4"); File.WriteAllText(target, "TARGET");
        var existing = await _host.Files.AddAsync(target, b.Id);
        var result = Assert.Single(await _host.Organization.OrganizeAsync(a.Id, (_, _) => Task.FromResult(ConflictResolution.Skip)));
        Assert.Equal(OrganizeOutcome.Failed, result.Outcome); Assert.Contains("已有分类记录", result.Error);
        Assert.Equal(source, (await _host.Files.GetByIdAsync(external.Id))!.AbsolutePath);
        Assert.Equal(b.Id, (await _host.Files.GetByIdAsync(existing.Id))!.CategoryId);
        Assert.Equal("SOURCE", File.ReadAllText(source)); Assert.Equal("TARGET", File.ReadAllText(target));
    }

    [Fact]
    public async Task ExternalImportReportsSuccessAndFailureWithoutModalCallbacks()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        var main = new MainViewModel(_host.Categories, _host.Files, _host.Organization, new FakePlaybackEngine());
        var modal = 0; var reports = new List<string>();
        main.FileList.ShowError = _ => modal++; main.FileList.ShowInfo = _ => modal++;
        main.FileList.ShowImportStatus = reports.Add;
        await main.FileList.ImportIntoAsync(new[] { _host.CreateTestFile("a.mp4") }, a);
        await main.FileList.ImportIntoAsync(new[] { Path.Combine(_host.RootDir, "missing.mp4") }, a);
        Assert.Equal(0, modal); Assert.Equal(2, reports.Count); Assert.Contains("失败", reports[1]);
    }

    [Theory]
    [InlineData(-5, 15, 0)]
    [InlineData(80, 15, 1)]
    [InlineData(180, 15, 2)]
    [InlineData(-5, 50, 2)]
    [InlineData(80, 50, 3)]
    [InlineData(180, 50, 4)]
    [InlineData(10, 100, 4)]
    public void PinnedInsertionSupportsGapsAndBothEndsAcrossRows(double x, double y, int index)
    {
        var bounds = new[] { new Rect(0, 0, 80, 30), new Rect(88, 0, 80, 30), new Rect(0, 36, 80, 30), new Rect(88, 36, 80, 30) };
        Assert.Equal(index, NavigationBar.InsertionIndex(bounds, new Point(x, y)));
    }

    private sealed class BlockingFactory(IDbContextFactory<AppDbContext> inner) : IDbContextFactory<AppDbContext>
    {
        public TaskCompletionSource<int> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public AppDbContext CreateDbContext()
        { Entered.TrySetResult(Environment.CurrentManagedThreadId); Release.Wait(TimeSpan.FromSeconds(4)); return inner.CreateDbContext(); }
    }
    [Fact]
    public async Task DatabaseWaitDoesNotBlockWpfDispatcher()
    {
        var a = await _host.Categories.CreateAsync("A", null);
        await WpfTestHost.RunAsync(async () =>
        {
            var factory = new BlockingFactory(_host.DbFactory);
            try
            {
                var thread = Environment.CurrentManagedThreadId;
                var query = new FileService(factory).GetByCategoryAsync(a.Id);
                Assert.NotEqual(thread, await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
                var pulse = false;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => pulse = true, DispatcherPriority.Background);
                Assert.True(pulse); Assert.False(query.IsCompleted);
                factory.Release.Set(); Assert.Empty(await query);
            }
            finally { factory.Release.Set(); factory.Release.Dispose(); }
        });
    }

    [Fact]
    public async Task NativeUncoveredHostPaintsBlackAfterResizeAndUnhooksOnDispose()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var window = CreateWindowEx(0, "static", "black verification", unchecked((int)0x80000000), -5000, -5000, 300, 200, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, window);
            var screen = GetDC(IntPtr.Zero); var dc = CreateCompatibleDC(screen); var bitmap = CreateCompatibleBitmap(screen, 300, 200); var previous = SelectObject(dc, bitmap);
            try
            {
                using var background = new NativeVideoBackground(); background.Attach(window);
                SetPixel(dc, 2, 2, 0xFFFFFF); SetPixel(dc, 298, 198, 0xFFFFFF);
                SendMessage(window, 0x0318, dc, IntPtr.Zero);
                Assert.Equal(0u, GetPixel(dc, 2, 2)); Assert.Equal(0u, GetPixel(dc, 298, 198));
                SetWindowPos(window, IntPtr.Zero, 0, 0, 220, 120, 0x0002 | 0x0004);
                SetPixel(dc, 218, 118, 0xFFFFFF);
                SendMessage(window, 0x0318, dc, IntPtr.Zero);
                Assert.Equal(0u, GetPixel(dc, 218, 118));
                background.Dispose();
                SetPixel(dc, 2, 2, 0xFFFFFF); SendMessage(window, 0x0318, dc, IntPtr.Zero);
                Assert.NotEqual(0u, GetPixel(dc, 2, 2));
            }
            finally { SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen); DestroyWindow(window); }
        });
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern uint SetPixel(IntPtr dc, int x, int y, uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
