using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Services;
using Serilog;

namespace NexusExplorer.Views;

public partial class PdfContinuousView : UserControl
{
    private PdfPreviewSession? _session;
    internal PdfPreviewSession? Session => _session;
    internal double Scale { get; private set; } = 1;
    internal int Rotation { get; private set; }
    internal Size EstimatedSize { get; private set; } = new(595, 842);
    internal event Action<int>? CurrentPageChanged;
    internal IReadOnlyList<PdfPageTile> RealizedPages => Descendants<PdfPageTile>(Pages)
        .Where(t => CategoryFilePanel.FindAncestor<ListBoxItem>(t) is { } row && Pages.ItemContainerGenerator.IndexFromContainer(row) >= 0).ToList();
    private bool _positioning;

    public PdfContinuousView() => InitializeComponent();

    internal async Task ShowAsync(PdfPreviewSession session, double scale, int rotation, Size estimate, int page)
    {
        var changed = !ReferenceEquals(_session, session);
        _session = session; Scale = scale; Rotation = rotation; EstimatedSize = estimate;
        if (changed) Pages.ItemsSource = Enumerable.Range(0, checked((int)session.PageCount)).ToArray();
        Pages.UpdateLayout();
        foreach (var tile in RealizedPages) tile.Refresh();
        GoToPage(page);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (!ReferenceEquals(_session, session)) return;
        await Task.WhenAll(RealizedPages.Select(t => t.RenderingTask));
    }

    internal void GoToPage(int index)
    {
        if (_session is null) return;
        _positioning = true;
        Pages.ScrollIntoView(index); Pages.UpdateLayout();
        if (Pages.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement row
            && Descendants<ScrollViewer>(Pages).FirstOrDefault() is { } scroll)
        {
            var point = row.TranslatePoint(new Point(), scroll);
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + point.Y);
        }
        _positioning = false;
    }

    internal void Clear()
    {
        _session = null;
        foreach (var tile in Descendants<PdfPageTile>(Pages)) tile.Cancel();
        Pages.ItemsSource = null;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_positioning || _session is null || e.VerticalChange == 0) return;
        var scroll = e.OriginalSource as ScrollViewer;
        if (scroll is null) return;
        foreach (var tile in Descendants<PdfPageTile>(Pages).Except(RealizedPages)) tile.Cancel();
        var first = RealizedPages.Select(t => (Tile: t, Y: t.TranslatePoint(new Point(), scroll).Y))
            .Select(p => (p.Tile, Visible: Math.Min(scroll.ViewportHeight, p.Y + p.Tile.ActualHeight) - Math.Max(0, p.Y)))
            .Where(p => p.Visible > 0).OrderByDescending(p => p.Visible).FirstOrDefault();
        if (first.Tile is not null) CurrentPageChanged?.Invoke(first.Tile.PageIndex);
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

/// <summary>Only realized page containers own images; recycling cancels the old page request.</summary>
public sealed class PdfPageTile : Border
{
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _status = new() { Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _request;
    private Size _size;
    private int _version;
    internal int PageIndex => DataContext is int index ? index : 0;
    internal BitmapSource? Image => _image.Source as BitmapSource;
    internal Task RenderingTask { get; private set; } = Task.CompletedTask;

    public PdfPageTile()
    {
        // A positive placeholder must exist before the first measure. Zero-height
        // async placeholders cause the panel to realize the entire document.
        Width = 595; Height = 842;
        Margin = new Thickness(8); HorizontalAlignment = HorizontalAlignment.Center; Background = Brushes.White;
        var grid = new Grid(); grid.Children.Add(_image); grid.Children.Add(_status); Child = grid;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => Refresh();
        DataContextChanged += (_, _) => { _size = default; Cancel(); if (IsLoaded) Refresh(); };
        Unloaded += (_, _) => Cancel();
    }

    internal void Cancel()
    { ++_version; _request?.Cancel(); _request?.Dispose(); _request = null; _image.Source = null; }

    internal void Refresh()
    {
        var owner = CategoryFilePanel.FindAncestor<PdfContinuousView>(this);
        if (owner?.Session is not { } session) return;
        Cancel(); _request = new(); var token = _request.Token; var version = _version; var index = PageIndex;
        SetSize(_size.Width > 0 ? _size : owner.EstimatedSize, owner);
        _status.Text = $"第 {index + 1} 页 · 加载中…";
        RenderingTask = RenderAsync();
        async Task RenderAsync()
        {
            try
            {
                var size = await session.GetPageSizeAsync(index, token);
                if (token.IsCancellationRequested || version != _version) return;
                _size = size; SetSize(size, owner);
                var dpi = VisualTreeHelper.GetDpi(this);
                var pixels = PdfPreview.PixelSize(size, owner.Scale, dpi.DpiScaleX, dpi.DpiScaleY);
                var image = await session.RenderAsync(index, pixels.Width, pixels.Height, token);
                if (token.IsCancellationRequested || version != _version) return;
                _image.Source = image; _status.Text = "";
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            { if (version == _version) { _status.Text = $"第 {index + 1} 页加载失败"; Log.Warning(ex, "PDF 连续阅读页面加载失败;页 {Page}", index + 1); } }
        }
    }

    private void SetSize(Size size, PdfContinuousView owner)
    {
        var rotated = owner.Rotation % 180 != 0;
        Width = (rotated ? size.Height : size.Width) * owner.Scale;
        Height = (rotated ? size.Width : size.Height) * owner.Scale;
        _image.Width = size.Width * owner.Scale; _image.Height = size.Height * owner.Scale;
        _image.LayoutTransform = new RotateTransform(owner.Rotation);
    }
}
