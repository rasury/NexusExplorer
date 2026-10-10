using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using NexusExplorer.Services;

namespace NexusExplorer.Views;

public partial class DocumentPreview : UserControl
{
    private sealed record EncodingItem(string Name, TextEncodingChoice Value);
    private CancellationTokenSource? _reading;
    private long _request;
    private long _mediaVersion = -1;
    private string? _path;
    private bool _markdown, _ready, _renderAllowed, _previewMode;
    private int _limit = TextPreviewReader.InitialBytes;
    private string? _renderedText;
    internal Task LoadingTask { get; private set; } = Task.CompletedTask;
    internal string SourceText => SourceEditor.Text;

    public DocumentPreview()
    {
        InitializeComponent();
        EncodingBox.ItemsSource = new[] { new EncodingItem("自动识别", TextEncodingChoice.Auto),
            new EncodingItem("UTF-8", TextEncodingChoice.Utf8), new EncodingItem("UTF-16 LE", TextEncodingChoice.Utf16LE),
            new EncodingItem("UTF-16 BE", TextEncodingChoice.Utf16BE), new EncodingItem("GB18030", TextEncodingChoice.GB18030) };
        EncodingBox.SelectedIndex = 0;
        _ready = true;
        Unloaded += (_, _) => Clear();
    }

    internal void SetFile(string path, bool markdown, long mediaVersion)
    {
        if (_path == path && _mediaVersion == mediaVersion) return;
        Clear(); _path = path; _mediaVersion = mediaVersion; _markdown = markdown;
        DocumentTitle.Text = Path.GetFileName(path); DocumentTitle.ToolTip = path;
        _ready = false; EncodingBox.SelectedIndex = 0; _previewMode = markdown; _ready = true;
        PreviewToggle.Visibility = markdown ? Visibility.Visible : Visibility.Collapsed;
        LoadingTask = LoadAsync();
    }

    internal void Clear()
    {
        ++_request; _reading?.Cancel(); _reading?.Dispose(); _reading = null;
        _path = null; _mediaVersion = -1; _limit = TextPreviewReader.InitialBytes;
        _renderedText = null; _renderAllowed = false; SourceEditor.Text = ""; MarkdownViewer.Document = null;
        DocumentStatus.Text = ""; LoadMoreButton.Visibility = Visibility.Collapsed;
    }

    private async Task LoadAsync()
    {
        if (_path is null) return;
        var path = _path; var request = ++_request;
        _reading?.Cancel(); _reading?.Dispose(); _reading = new();
        var token = _reading.Token;
        DocumentStatus.Text = "读取文档中…";
        try
        {
            var choice = (EncodingBox.SelectedItem as EncodingItem)?.Value ?? TextEncodingChoice.Auto;
            var limit = _limit;
            var preview = await Task.Run(() => TextPreviewReader.ReadAsync(path, choice, limit, token), token);
            if (token.IsCancellationRequested || request != _request) return;
            SourceEditor.Text = preview.Text;
            _renderedText = null; MarkdownViewer.Document = null;
            _renderAllowed = preview.Text.Length <= 100_000 && preview.Text.Count(c => c == '\n') <= 3000;
            PreviewToggle.IsEnabled = _renderAllowed;
            if (!_renderAllowed) _previewMode = false;
            LoadMoreButton.Visibility = preview.Truncated && _limit < TextPreviewReader.MaximumBytes ? Visibility.Visible : Visibility.Collapsed;
            DocumentStatus.Text = $"{preview.EncodingName} · {preview.TotalBytes:N0} 字节"
                + (preview.Truncated ? $" · 当前预览前 {_limit / 1024 / 1024} MiB" : "")
                + (_markdown && !_renderAllowed ? " · 文档较大，使用原文预览" : "");
            ApplyMode();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (request != _request) return;
            SourceEditor.Text = ""; MarkdownViewer.Document = null;
            DocumentStatus.Text = ex is DecoderFallbackException ? "当前编码无法解码，请切换编码。" : $"无法读取文档：{ex.Message}";
        }
    }

    private void OnEncodingChanged(object sender, SelectionChangedEventArgs e)
    { if (_ready && _path is not null) LoadingTask = LoadAsync(); }
    private void OnLoadMore(object sender, RoutedEventArgs e)
    { _limit = Math.Min(_limit * 2, TextPreviewReader.MaximumBytes); LoadingTask = LoadAsync(); }
    private void OnPreviewModeChanged(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            _previewMode = !_previewMode;
            ApplyMode();
        }
    }

    private void ApplyMode()
    {
        var render = _markdown && _renderAllowed && _previewMode;
        PreviewToggle.Content = render ? "排版预览" : "查看原文";
        SourceEditor.Visibility = render ? Visibility.Collapsed : Visibility.Visible;
        MarkdownViewer.Visibility = render ? Visibility.Visible : Visibility.Collapsed;
        if (!render || _path is null || _renderedText == SourceEditor.Text) return;
        try
        {
            var renderer = new MdXaml.Markdown { AssetPathRoot = Path.GetDirectoryName(_path),
                HyperlinkCommand = new RelayCommand<object>(OpenLink), DisabledContextMenu = true };
            var documentStyle = new Style(typeof(FlowDocument));
            documentStyle.Setters.Add(new Setter(TextElement.FontFamilyProperty, FindResource("FontFamilyMain")));
            documentStyle.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("BrushPrimaryText")));
            documentStyle.Setters.Add(new Setter(FlowDocument.FontSizeProperty, 14d));
            documentStyle.Setters.Add(new Setter(FlowDocument.PagePaddingProperty, new Thickness(8)));
            renderer.DocumentStyle = documentStyle;
            var linkStyle = new Style(typeof(Hyperlink));
            linkStyle.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("BrushAccentBlue")));
            renderer.LinkStyle = linkStyle;
            Style Heading(double size)
            {
                var style = new Style(typeof(Paragraph));
                style.Setters.Add(new Setter(TextElement.FontSizeProperty, size));
                style.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.SemiBold));
                style.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 8, 0, 12)));
                return style;
            }
            renderer.Heading1Style = Heading(28); renderer.Heading2Style = Heading(24); renderer.Heading3Style = Heading(20);
            renderer.Heading4Style = Heading(18); renderer.Heading5Style = Heading(16); renderer.Heading6Style = Heading(14);
            var document = renderer.Transform(SourceEditor.Text);
            StyleBlocks(document.Blocks);
            MarkdownViewer.Document = document;
            _renderedText = SourceEditor.Text;
        }
        catch (Exception ex)
        {
            _previewMode = false; ApplyMode();
            DocumentStatus.Text = $"排版预览失败，已显示原文：{ex.Message}";
        }
    }

    private static void StyleBlocks(BlockCollection blocks)
    {
        void StyleElement(DependencyObject element)
        {
            if (element is ICSharpCode.AvalonEdit.TextEditor editor)
            {
                editor.SetResourceReference(Control.BackgroundProperty, "BrushSecondaryBg");
                editor.SetResourceReference(Control.ForegroundProperty, "BrushPrimaryText");
                editor.SyntaxHighlighting = null;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>()) StyleElement(child);
        }
        foreach (var block in blocks)
        {
            if (block is BlockUIContainer container) StyleElement(container.Child);
            if (block is Paragraph paragraph)
                foreach (var inline in paragraph.Inlines.OfType<InlineUIContainer>()) StyleElement(inline.Child);
            if (block is Section section) StyleBlocks(section.Blocks);
            if (block is System.Windows.Documents.List list)
                foreach (var item in list.ListItems) StyleBlocks(item.Blocks);
            if (block is Table table)
            {
                table.CellSpacing = 0;
                foreach (var group in table.RowGroups)
                    for (var row = 0; row < group.Rows.Count; row++)
                        foreach (var cell in group.Rows[row].Cells)
                        {
                            cell.Padding = new Thickness(8);
                            cell.BorderThickness = new Thickness(0, 0, 0, 1);
                            cell.SetResourceReference(TableCell.BorderBrushProperty, "BrushDivider");
                            if (row == 0) cell.SetResourceReference(TextElement.BackgroundProperty, "BrushSecondaryBg");
                            StyleBlocks(cell.Blocks);
                        }
            }
        }
    }

    private void OpenLink(object? value)
    {
        if (value is not string link || _path is null) return;
        try
        {
            var uri = Uri.TryCreate(link, UriKind.Absolute, out var absolute) ? absolute
                : new Uri(new Uri(Path.GetDirectoryName(_path)! + Path.DirectorySeparatorChar), link);
            if (uri.Scheme is "http" or "https") Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            else if (uri.IsFile)
            {
                // Local document/image links open only after an explicit user click.
                var extension = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
                if (extension is ".txt" or ".md" or ".pdf" or ".png" or ".jpg" or ".jpeg" or ".jfif" or ".gif" or ".webp")
                    Process.Start(new ProcessStartInfo(uri.LocalPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { DocumentStatus.Text = $"无法打开链接：{ex.Message}"; }
    }
}
