using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NexusExplorer.Models;
using NexusExplorer.Services;
using Serilog;

namespace NexusExplorer.Views;

/// <summary>A single delayed preview for a virtualized list, independent of its selection and player.</summary>
internal sealed class FileHoverPreview : IDisposable
{
    internal static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(500);
    private readonly ListBox _list;
    private readonly DispatcherTimer _delay;
    private readonly Func<string, CancellationToken, Task<ThumbnailResult>> _load;
    private readonly Func<ListBoxItem, bool> _isHovered;
    private ListBoxItem? _row;
    private FileItem? _file;
    private string? _path;
    private ToolTip? _popup;
    private FileThumbnailPreview? _view;
    private CancellationTokenSource? _request;
    private int _version;
    private bool _suppressed;
    private Point _lastPointer;
    private Window? _owner;
    internal bool IsOpen => _popup?.IsOpen == true;
    internal FileThumbnailPreview? View => _view;

    internal FileHoverPreview(ListBox list,
        Func<string, CancellationToken, Task<ThumbnailResult>>? loader = null, Func<ListBoxItem, bool>? isHovered = null)
    {
        _list = list;
        _load = loader ?? ((path, token) => ThumbnailService.Shared.GetAsync(path, token));
        _isHovered = isHovered ?? (row => row.IsMouseOver);
        _delay = new DispatcherTimer(DispatcherPriority.Background, list.Dispatcher) { Interval = HoverDelay };
        _delay.Tick += OnDelay;
        list.PreviewMouseMove += OnMouseMove;
        list.MouseLeave += OnMouseLeave;
        list.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown), true);
        list.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnMouseWheel), true);
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        list.ContextMenuOpening += OnContextMenu;
        list.PreviewDragEnter += OnDrag;
        list.SizeChanged += OnSizeChanged;
        list.IsVisibleChanged += OnVisibilityChanged;
        list.IsEnabledChanged += OnVisibilityChanged;
        list.Unloaded += OnUnloaded;
        list.Loaded += OnLoaded;
        if (list.IsLoaded) AttachOwner();
    }
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(_list);
        if (_suppressed && (point - _lastPointer).Length < 2) return;
        _lastPointer = point; _suppressed = false;
        if (e.LeftButton != MouseButtonState.Released || e.RightButton != MouseButtonState.Released
            || CategoryFilePanel.IsControlChrome(e.OriginalSource as DependencyObject)) { Cancel(); return; }
        Hover(CategoryFilePanel.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject));
    }
    internal void Hover(ListBoxItem? row)
    {
        if (row?.DataContext is not FileItem file) { Cancel(); return; }
        if (_row == row && _file == file && _path == file.AbsolutePath) return;
        Cancel(); _row = row; _file = file; _path = file.AbsolutePath;
        row.DataContextChanged += OnRowChanged; row.Unloaded += OnRowUnloaded;
        _delay.Start();
    }
    private bool StillHovered() => _row is { IsLoaded: true, IsVisible: true, IsEnabled: true }
        && _row.DataContext == _file && _file?.AbsolutePath == _path && _isHovered(_row);
    private async void OnDelay(object? sender, EventArgs e)
    {
        _delay.Stop();
        if (!StillHovered()) { Cancel(); return; }
        var version = _version; var file = _file!; var path = _path!;
        _request = new CancellationTokenSource(); var token = _request.Token;
        _view ??= new FileThumbnailPreview();
        if (_popup is null)
        {
            _popup = new ToolTip { Content = _view, Placement = PlacementMode.Right, HorizontalOffset = 8,
                StaysOpen = true, IsHitTestVisible = false, Padding = new Thickness(0), HasDropShadow = false,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
                MaxHeight = SystemParameters.WorkArea.Height - 32 };
            _popup.SetResourceReference(FrameworkElement.StyleProperty, "MaterialDesignToolTip");
            ElevationAssist.SetElevation(_popup, Elevation.Dp0);
            _popup.SetResourceReference(Control.ForegroundProperty, "BrushPrimaryText");
        }
        _view.ShowLoading(file); _popup.PlacementTarget = _row; _popup.IsOpen = true;
        try
        {
            var result = await _load(path, token);
            if (version == _version && !token.IsCancellationRequested && StillHovered()) _view.ShowResult(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug(ex, "文件悬停预览失败 {Path}", path);
            if (version == _version && StillHovered()) _view.ShowResult(new(null, ThumbnailStatus.Unavailable));
        }
    }
    internal void Cancel()
    {
        _version++; _delay.Stop(); _request?.Cancel(); _request?.Dispose(); _request = null;
        if (_popup is not null) { _popup.IsOpen = false; _popup.PlacementTarget = null; }
        _view?.Clear();
        if (_row is not null) { _row.DataContextChanged -= OnRowChanged; _row.Unloaded -= OnRowUnloaded; }
        _row = null; _file = null; _path = null;
    }
    private void Suppress() { _suppressed = true; Cancel(); }
    private void OnMouseDown(object sender, MouseButtonEventArgs e) => Suppress();
    private void OnMouseWheel(object sender, MouseWheelEventArgs e) => Suppress();
    private void OnMouseLeave(object sender, MouseEventArgs e) => Cancel();
    private void OnContextMenu(object sender, ContextMenuEventArgs e) => Suppress();
    private void OnDrag(object sender, DragEventArgs e) => Suppress();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    { if (e.VerticalChange != 0 || e.HorizontalChange != 0 || e.ViewportHeightChange != 0) Suppress(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Suppress();
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (!_list.IsVisible || !_list.IsEnabled) Cancel(); }
    private void OnRowChanged(object sender, DependencyPropertyChangedEventArgs e) => Cancel();
    private void OnRowUnloaded(object sender, RoutedEventArgs e) => Cancel();
    private void OnLoaded(object sender, RoutedEventArgs e) => AttachOwner();
    private void AttachOwner()
    {
        DetachOwner(); _owner = Window.GetWindow(_list);
        if (_owner is not null) { _owner.Deactivated += OnOwnerChanged; _owner.LocationChanged += OnOwnerChanged; }
    }
    private void DetachOwner()
    {
        if (_owner is not null) { _owner.Deactivated -= OnOwnerChanged; _owner.LocationChanged -= OnOwnerChanged; _owner = null; }
    }
    private void OnOwnerChanged(object? sender, EventArgs e) => Suppress();
    private void OnUnloaded(object sender, RoutedEventArgs e) { Cancel(); DetachOwner(); }
    public void Dispose()
    {
        Cancel(); DetachOwner(); _delay.Tick -= OnDelay;
        _list.PreviewMouseMove -= OnMouseMove; _list.MouseLeave -= OnMouseLeave;
        _list.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown));
        _list.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnMouseWheel));
        _list.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        _list.ContextMenuOpening -= OnContextMenu; _list.PreviewDragEnter -= OnDrag;
        _list.SizeChanged -= OnSizeChanged; _list.IsVisibleChanged -= OnVisibilityChanged; _list.IsEnabledChanged -= OnVisibilityChanged;
        _list.Unloaded -= OnUnloaded; _list.Loaded -= OnLoaded;
    }
}
