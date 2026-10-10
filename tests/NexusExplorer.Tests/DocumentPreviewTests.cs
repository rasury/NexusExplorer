using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusExplorer.Infrastructure;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Views;
using SixLabors.ImageSharp.PixelFormats;

namespace NexusExplorer.Tests;

[Collection("NativePlaybackLogging")]
public sealed class DocumentPreviewTests
{
    [Theory]
    [InlineData("sample.JFIF", MediaKind.Image)]
    [InlineData("sample.TXT", MediaKind.Text)]
    [InlineData("sample.MD", MediaKind.Markdown)]
    public void NewExtensionsAreRecognized(string path, MediaKind expected)
        => Assert.Equal(expected, PlayerViewModel.GetMediaKind(path));

    [Fact]
    public async Task JfifDisplaysInMainPreviewAndThumbnailWithoutKeepingTheFileOpen()
    {
        using var host = new TestHost(); var category = await host.Categories.CreateAsync("image", null);
        var path = Path.Combine(host.RootDir, "picture.JFIF");
        using (var image = new SixLabors.ImageSharp.Image<Rgba32>(640, 320, new Rgba32(50, 100, 200)))
            SixLabors.ImageSharp.ImageExtensions.SaveAsJpeg(image, path);
        var file = await host.Files.AddAsync(path, category.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var main = new MainViewModel(host.Categories, host.Files, host.Organization, new FakePlaybackEngine());
            await main.Player.PlayFileAsync(file);
            Assert.Equal(MediaKind.Image, main.Player.Kind);
            Assert.Equal(640, Assert.IsAssignableFrom<BitmapSource>(main.Player.ImageSource).PixelWidth);
            var thumb = await Task.Run(() => ThumbnailDecoder.Decode(path));
            Assert.NotNull(thumb); Assert.Equal(320, thumb.PixelWidth); Assert.True(thumb.IsFrozen);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            main.Player.ReleaseImagePreview();
        });
    }

    [Theory]
    [InlineData("utf8")][InlineData("utf8-bom")][InlineData("utf16-le")][InlineData("utf16-be")][InlineData("gb18030")]
    public async Task ReaderDetectsChineseEncodingsAndReleasesTheSource(string mode)
    {
        using var host = new TestHost(); var path = Path.Combine(host.RootDir, "text.txt");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = mode switch { "utf8" => new UTF8Encoding(false), "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => Encoding.Unicode, "utf16-be" => Encoding.BigEndianUnicode, _ => Encoding.GetEncoding("GB18030") };
        const string text = "中文预览\r\n第二行 ABC";
        await File.WriteAllTextAsync(path, text, encoding);
        var preview = await TextPreviewReader.ReadAsync(path, TextEncodingChoice.Auto, TextPreviewReader.InitialBytes, default);
        Assert.Equal(text, preview.Text); Assert.False(preview.Truncated);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task ReaderBoundsLargeFilesAndDoesNotSplitUtf8Characters()
    {
        using var host = new TestHost(); var path = host.CreateTestFile("large.txt", "你好世界");
        var preview = await TextPreviewReader.ReadAsync(path, TextEncodingChoice.Auto, 5, default);
        Assert.True(preview.Truncated); Assert.Equal("你", preview.Text);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TextPreviewReader.ReadAsync(path, TextEncodingChoice.Auto, 5, cancelled.Token));
    }

    [Theory]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public async Task MarkdownRendersHeadingsTablesAndSwitchesToReadOnlySource(UiThemeMode theme)
    {
        using var host = new TestHost(); var path = host.CreateTestFile("notes.md", "# 中文标题\n\n正文 **强调**\n\n- 项目\n\n| 列1 | 列2 |\n|---|---|\n| A | B |\n\n```csharp\nvar x = 1;\n```\n");
        await WpfTestHost.RunAsync(async () =>
        {
            UiThemeService.Apply(theme);
            var view = new DocumentPreview();
            try
            {
                view.SetFile(path, true, 1); await view.LoadingTask;
                view.Measure(new Size(700, 500)); view.Arrange(new Rect(0, 0, 700, 500)); view.UpdateLayout();
                var rendered = (FlowDocumentScrollViewer)view.FindName("MarkdownViewer");
                Assert.NotNull(rendered.Document); Assert.Contains(rendered.Document.Blocks, b => b is Table);
                Assert.Contains("中文标题", new TextRange(rendered.Document.ContentStart, rendered.Document.ContentEnd).Text);
                Assert.Equal(((SolidColorBrush)view.FindResource("BrushPrimaryText")).Color, ((SolidColorBrush)rendered.Document.Foreground).Color);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, 700, 500));
                var bitmap = new RenderTargetBitmap(700, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var folder = Path.Combine(Path.GetTempPath(), "nexus-feature-checks"); Directory.CreateDirectory(folder);
                using (var screenshot = File.Create(Path.Combine(folder, $"markdown-{theme}.png"))) encoder.Save(screenshot);
                ((Button)view.FindName("PreviewToggle")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, rendered.Visibility);
                Assert.Equal(await File.ReadAllTextAsync(path), view.SourceText);
                Assert.True(((ICSharpCode.AvalonEdit.TextEditor)view.FindName("SourceEditor")).IsReadOnly);
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally { view.Clear(); UiThemeService.Apply(UiThemeMode.Light); }
        });
    }

    [Fact]
    public async Task NewDocumentRequestWinsAndLargeMarkdownUsesBoundedSourceView()
    {
        using var host = new TestHost(); var first = host.CreateTestFile("large.md", new string('a', 200_000));
        var second = host.CreateTestFile("next.txt", "最新文档");
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new DocumentPreview();
            view.SetFile(first, true, 1); await view.LoadingTask;
            Assert.False(((Button)view.FindName("PreviewToggle")).IsEnabled);
            view.SetFile(first, true, 2); var old = view.LoadingTask;
            view.SetFile(second, false, 3); await view.LoadingTask; await old;
            Assert.Equal("最新文档", view.SourceText);
            view.Clear(); Assert.Equal("", view.SourceText);
        });
    }

    [Fact]
    public async Task TextDocumentsParticipateInClassificationQueueAndImportLabelsAreUpdated()
    {
        using var host = new TestHost(); var source = await host.Categories.CreateAsync("A", null); var target = await host.Categories.CreateAsync("B", null);
        var first = await host.Files.AddAsync(host.CreateTestFile("a.txt", "A"), source.Id);
        var second = await host.Files.AddAsync(host.CreateTestFile("b.md", "# B"), source.Id);
        await WpfTestHost.RunAsync(async () =>
        {
            var engine = new FakePlaybackEngine(); var main = new MainViewModel(host.Categories, host.Files, host.Organization, engine);
            var panel = new PlayerPanel(); panel.Initialize(main);
            await main.SelectCategoryAsync(source); await main.SelectFileAsync(first);
            var document = (DocumentPreview)panel.FindName("DocumentArea"); await document.LoadingTask;
            Assert.Equal("A", document.SourceText);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("DocumentButtonsRow")).Visibility);
            await main.FileList.RecategorizeAsync(first, target); await document.LoadingTask;
            Assert.Equal(second.Id, main.CurrentFile!.Id); Assert.Equal("# B", document.SourceText); Assert.Empty(engine.Played);
            var categoryPanel = new CategoryFilePanel();
            var menu = ((TreeView)categoryPanel.FindName("CategoryTree")).ContextMenu;
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "导入文件"));
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "导入文件夹"));
            panel.Detach();
        });
    }
}
