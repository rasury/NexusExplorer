using System.IO;
using System.Windows;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;

namespace NexusExplorer.Tests;

public class WindowLifecycleTests
{
    [Fact]
    public async Task RealWindowCloseWaitsForNativePlaybackRelease()
    {
        using var host = new TestHost();
        var category = await host.Categories.CreateAsync("A", null);
        var path = SyntheticMedia.WriteAvi(Path.Combine(host.RootDir, "close.avi"));
        var file = await host.Files.AddAsync(path, category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            using var engine = new MediaPlayerService(System.Windows.Threading.Dispatcher.CurrentDispatcher) { HardwareDecoding = false };
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var window = new MainWindow(main, host.Categories, host.Files, host.Organization, host.RecycleBin, engine)
            { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -5000, Top = -5000 };
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show(); await engine.InitializeAsync();
            await main.SelectCategoryAsync(category); await main.SelectFileAsync(file);
            window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Null(engine.CurrentPath);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
    }
}
