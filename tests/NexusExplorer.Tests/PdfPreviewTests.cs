using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Infrastructure;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using NexusExplorer.Views.Dialogs;
using MaterialDesignThemes.Wpf;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public sealed class PdfPreviewTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", "Pdf", name);
    private static string CopyFixture(TestHost host, string name = "reading.pdf")
    {
        var path = Path.Combine(host.RootDir, "中文文档 " + name); File.Copy(Fixture(name), path); return path;
    }

    [Fact]
    public async Task RealWindowsRendererHandlesTextLandscapeScanCacheAndWaitableRelease()
    {
        using var host = new TestHost(); var path = CopyFixture(host);
        var session = await PdfPreviewSession.OpenAsync(path, null, default);
        Assert.Equal(12u, session.PageCount);
        var portrait = await session.GetPageSizeAsync(0, default); var landscape = await session.GetPageSizeAsync(1, default);
        Assert.True(portrait.Height > portrait.Width); Assert.True(landscape.Width > landscape.Height);
        var first = await session.RenderAsync(0, 595, 842, default);
        Assert.True(first.IsFrozen); Assert.Equal(595, first.PixelWidth);
        Assert.Same(first, await session.RenderAsync(0, 595, 842, default));
        var scanned = await session.RenderAsync(2, 595, 842, default);
        var colors = new byte[scanned.PixelWidth * scanned.PixelHeight * 4]; scanned.CopyPixels(colors, scanned.PixelWidth * 4, 0);
        Assert.True(colors.Distinct().Count() > 100);
        for (var page = 0; page < 7; page++) await session.RenderAsync(page, 1600, 2000, default);
        Assert.InRange(session.CacheBytes, 1, PdfPreviewSession.CacheBudget);
        var rendering = session.RenderAsync(11, 2048, 2048, default);
        await session.DisposeAsync(); await rendering;
        Assert.Equal(0, session.CacheBytes);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task PasswordCorruptionAndThumbnailReleaseAreRealNativeChecks()
    {
        using var host = new TestHost(); var password = CopyFixture(host, "password.pdf");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => PdfPreviewSession.OpenAsync(password, "wrong", default));
        Assert.Equal(unchecked((int)0x8007052b), error.HResult);
        await using (var unlocked = await PdfPreviewSession.OpenAsync(password, "nexus-test", default))
        { Assert.Equal(1u, unlocked.PageCount); Assert.NotNull(await unlocked.RenderAsync(0, 320, 452, default)); }
        using (var exclusive = File.Open(password, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var invalid = host.CreateTestFile("broken.pdf", "not a PDF");
        await Assert.ThrowsAnyAsync<Exception>(() => PdfPreviewSession.OpenAsync(invalid, null, default));
        using (var exclusive = File.Open(invalid, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var pdf = CopyFixture(host); var thumbnail = await Task.Run(() => ThumbnailDecoder.Decode(pdf));
        Assert.NotNull(thumbnail); Assert.True(thumbnail.IsFrozen); Assert.InRange(Math.Max(thumbnail.PixelWidth, thumbnail.PixelHeight), 1, 320);
        using var released = File.Open(pdf, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task ReaderNavigatesRotatesZoomsAndIgnoresOldRequests(UiThemeMode theme)
    {
        using var host = new TestHost(); var path = CopyFixture(host);
        await WpfTestHost.RunAsync(async () =>
        {
            UiThemeService.Apply(theme);
            var view = new PdfPreview(); var window = new Window { Content = view, Width = 800, Height = 650, ShowInTaskbar = false };
            window.Show(); window.UpdateLayout();
            try
            {
                view.SetFile(path, 1); await view.LoadingTask;
                Assert.Equal(12u, view.PageCount); Assert.NotNull(view.DisplayedImage);
                view.GoToPage(2); await view.RenderingTask;
                Assert.Equal(2, view.PageNumber); Assert.True(view.DisplayedImage!.PixelWidth > view.DisplayedImage.PixelHeight);
                ((Button)((Panel)view.FindName("ReadingControls")).Children.OfType<Button>().Single(b => Equals(b.ToolTip, "顺时针旋转 90°"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.RenderingTask;
                Assert.Equal(90, ((RotateTransform)((Image)view.FindName("PageImage")).LayoutTransform).Angle);
                view.GoToPage(999); Assert.Equal(2, view.PageNumber);
                var old = view.RenderingTask;
                view.GoToPage(8); view.GoToPage(3); await view.RenderingTask; await old;
                Assert.Equal(3, view.PageNumber); Assert.NotNull(view.DisplayedImage);
                var pageImage = (Image)view.FindName("PageImage"); var beforeZoom = pageImage.Width;
                ((Panel)view.FindName("ReadingControls")).Children.OfType<Button>().Single(b => Equals(b.ToolTip, "放大")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250); await view.RenderingTask;
                Assert.True(pageImage.Width > beforeZoom);
                ((Panel)view.FindName("ReadingControls")).Children.OfType<Button>().Single(b => Equals(b.ToolTip, "适合页面")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.RenderingTask;
                var bounds = PdfPreview.PixelSize(new Size(10000, 5000), 8, 2, 2);
                Assert.Equal((4096, 2048), bounds);
                view.UpdateLayout();
                await Task.Delay(250); await view.RenderingTask; view.UpdateLayout();
                var image = new RenderTargetBitmap(800, 650, 96, 96, PixelFormats.Pbgra32); image.Render(view);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                var folder = Path.Combine(Path.GetTempPath(), "nexus-feature-checks"); Directory.CreateDirectory(folder);
                using (var output = File.Create(Path.Combine(folder, $"pdf-{theme}.png"))) encoder.Save(output);
                view.SetFile(host.CreateTestFile("bad.pdf", "invalid"), 2); await view.LoadingTask;
                Assert.Null(view.DisplayedImage); Assert.Contains("无法读取 PDF", ((TextBlock)view.FindName("DocumentStatus")).Text);
                view.SetFile(path, 3); var opening = view.LoadingTask;
                await view.ClearAsync(); await opening;
                Assert.Null(view.DisplayedImage); Assert.Equal(0u, view.PageCount);
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally { await view.ClearAsync(); window.Close(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }

    [Fact]
    public async Task ProtectedPdfUsesMaskedMaterialDialogAndCanRetryThePassword()
    {
        using var host = new TestHost(); var path = CopyFixture(host, "password.pdf");
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new PdfPreview(); var dialogHost = new DialogHost { Content = view, Identifier = Guid.NewGuid().ToString() };
            var owner = new Window { Content = dialogHost, Width = 800, Height = 650, ShowInTaskbar = false };
            owner.Show(); MaterialDialogService.Register(dialogHost);
            try
            {
                view.SetFile(path, 1); await view.LoadingTask;
                var passwordButton = (Button)view.FindName("PasswordButton");
                Assert.Equal(Visibility.Visible, passwordButton.Visibility);
                foreach (var password in new[] { "wrong", "nexus-test" })
                {
                    passwordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Window? shell = null;
                    await BoundedDialogTests.Until(() => (shell = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => ReferenceEquals(w.Owner, owner)))?.Content is DialogHost { IsOpen: true, DialogContent: DialogSurface { IsLoaded: true } });
                    var surface = (DialogSurface)((DialogHost)shell!.Content).DialogContent!;
                    var body = (StackPanel)((ScrollViewer)surface.Children[0]).Content;
                    var box = body.Children.OfType<PasswordBox>().Single(); box.Password = password;
                    ((Panel)surface.Children[1]).Children.OfType<Button>().Single(b => Equals(b.Content, "确定")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await BoundedDialogTests.Until(() => !shell.IsVisible);
                    await view.LoadingTask;
                    if (password == "wrong") Assert.Contains("密码错误", ((TextBlock)view.FindName("DocumentStatus")).Text);
                }
                Assert.Equal(1u, view.PageCount); Assert.NotNull(view.DisplayedImage);
                Assert.Equal(Visibility.Collapsed, passwordButton.Visibility);
            }
            finally { await view.ClearAsync(); MaterialDialogService.CancelAll(); MaterialDialogService.Unregister(dialogHost); owner.Close(); MaterialDialogService.BeginSession(); }
        });
    }

    [Fact]
    public async Task PdfClassificationAdvancesQueueAndPhysicalRenameDeleteReleasePreview()
    {
        using var host = new TestHost(); var source = await host.Categories.CreateAsync("A", null); var target = await host.Categories.CreateAsync("B", null);
        var firstPath = Path.Combine(host.RootDir, "a.pdf"); File.Copy(Fixture("reading.pdf"), firstPath);
        var first = await host.Files.AddAsync(firstPath, source.Id);
        var second = await host.Files.AddAsync(host.CreateTestFile("next.txt", "next"), source.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var engine = new FakePlaybackEngine(); var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var panel = new PlayerPanel(); panel.Initialize(main);
            try
            {
                await main.SelectCategoryAsync(source); await main.SelectFileAsync(first);
                var view = (PdfPreview)panel.FindName("PdfArea"); await view.LoadingTask;
                Assert.Equal(MediaKind.Pdf, main.Player.Kind); Assert.NotNull(view.DisplayedImage);
                await main.FileList.RecategorizeAsync(first, target);
                Assert.Equal(second.Id, main.CurrentFile!.Id); Assert.Null(view.DisplayedImage); Assert.Empty(engine.Played);
                await main.SelectFileAsync((await host.Files.GetByIdAsync(first.Id))!); await view.LoadingTask;
                view.GoToPage(3);
                var renamed = await host.Files.RenameAsync(first.Id, "renamed.pdf");
                Assert.Null(view.DisplayedImage); Assert.True(File.Exists(renamed.AbsolutePath));
                using (var exclusive = File.Open(renamed.AbsolutePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                await main.SelectFileAsync(renamed); await view.LoadingTask;
                await host.Files.DeleteAsync(first.Id, host.RecycleBin);
                Assert.False(File.Exists(renamed.AbsolutePath)); Assert.Null(view.DisplayedImage);
            }
            finally { panel.Detach(); await ((PdfPreview)panel.FindName("PdfArea")).ClearAsync(); }
        });
    }
}
