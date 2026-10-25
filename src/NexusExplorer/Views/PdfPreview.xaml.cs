using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Services;
using NexusExplorer.Views.Dialogs;
using Serilog;

namespace NexusExplorer.Views;

public partial class PdfPreview : UserControl
{
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _opening, _rendering;
    private PdfPreviewSession? _session;
    private Task<PdfPreviewSession>? _nativeOpen;
    private Task _release = Task.CompletedTask;
    private string? _path;
    private long _mediaVersion = -1, _version, _renderVersion;
    private int _page, _rotation;
    private double _scale = 1;
    private int _fit; // 0: page, 1: width, 2: manual
    private bool _continuous;
    internal bool IsContinuous => _continuous;
    internal Task LoadingTask { get; private set; } = Task.CompletedTask;
    internal Task RenderingTask { get; private set; } = Task.CompletedTask;
    internal int PageNumber => _page + 1;
    internal uint PageCount => _session?.PageCount ?? 0;
    internal BitmapSource? DisplayedImage => PageImage.Source as BitmapSource;

    public PdfPreview()
    {
        InitializeComponent();
        ContinuousArea.CurrentPageChanged += page =>
        {
            if (!_continuous || _session is null) return;
            _page = page;
            if (!PageBox.IsKeyboardFocused) PageBox.Text = PageNumber.ToString();
            PreviousPageButton.IsEnabled = page > 0; NextPageButton.IsEnabled = page + 1 < PageCount;
            DocumentStatus.Text = $"第 {PageNumber} 页，共 {PageCount} 页";
        };
        _debounce = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(180) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RenderingTask = RenderPageAsync(); };
        Unloaded += async (_, _) => await ClearAsync();
    }

    internal void SetFile(string path, long mediaVersion)
    {
        if (_path == path && _mediaVersion == mediaVersion) return;
        _release = ClearAsync(); _path = path; _mediaVersion = mediaVersion;
        _page = _rotation = 0; _fit = _continuous ? 1 : 0; _scale = 1;
        DocumentTitle.Text = Path.GetFileName(path); DocumentTitle.ToolTip = path;
        LoadingTask = OpenAsync(null);
    }

    internal Task ClearAsync()
    {
        ++_version; ++_renderVersion; _debounce.Stop();
        _opening?.Cancel(); _opening?.Dispose(); _opening = null;
        _rendering?.Cancel(); _rendering?.Dispose(); _rendering = null;
        var opening = _nativeOpen; _nativeOpen = null;
        var session = _session; _session = null;
        ContinuousArea.Clear();
        _path = null; _mediaVersion = -1; PageImage.Source = null;
        ReadingControls.IsEnabled = false; PasswordButton.Visibility = Visibility.Collapsed;
        PageCountText.Text = "/ 0"; DocumentStatus.Text = "";
        _release = ReleaseAsync(_release, opening, session);
        return _release;
    }

    private static async Task ReleaseAsync(Task previous, Task<PdfPreviewSession>? opening, PdfPreviewSession? session)
    {
        await previous;
        if (session is not null) await session.DisposeAsync();
        if (opening is not null)
        {
            try { await (await opening).DisposeAsync(); }
            catch (Exception ex) { Log.Debug(ex, "PDF 打开请求结束，释放预览资源"); }
        }
    }

    private async Task OpenAsync(string? password)
    {
        var path = _path; var version = _version;
        if (path is null) return;
        await _release;
        if (version != _version) return;
        _opening?.Dispose(); _opening = new(); var token = _opening.Token;
        DocumentStatus.Text = "读取 PDF 中…"; PasswordButton.Visibility = Visibility.Collapsed;
        try
        {
            var opening = PdfPreviewSession.OpenAsync(path, password, token); _nativeOpen = opening;
            var session = await opening;
            if (version != _version || token.IsCancellationRequested) { await session.DisposeAsync(); return; }
            _session = session; _nativeOpen = null; ReadingControls.IsEnabled = true;
            PageCountText.Text = $"/ {session.PageCount}";
            RenderingTask = RenderPageAsync(); await RenderingTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version != _version) return;
            Log.Warning(ex, "PDF 读取失败 {Path}", path);
            var passwordError = ex.HResult == unchecked((int)0x8007052b);
            PasswordButton.Visibility = passwordError ? Visibility.Visible : Visibility.Collapsed;
            DocumentStatus.Text = passwordError ? (password is null ? "此 PDF 需要密码。" : "密码错误，请重新输入。") : $"无法读取 PDF：{ex.Message}";
        }
    }

    internal void GoToPage(int number)
    {
        if (_session is null) return;
        if (number < 1 || number > _session.PageCount)
        { DocumentStatus.Text = $"请输入 1～{_session.PageCount} 的页码。"; PageBox.Text = PageNumber.ToString(); return; }
        if (_continuous) { _page = number - 1; PageBox.Text = number.ToString(); ContinuousArea.GoToPage(_page); return; }
        if (_page == number - 1 && PageImage.Source is not null) return;
        _page = number - 1; PageImage.Source = null; PageScroll.ScrollToHome();
        ScheduleRender(false);
    }

    private void ScheduleRender(bool delay = true)
    {
        ++_renderVersion; _rendering?.Cancel(); _debounce.Stop();
        if (_session is null) return;
        if (delay) _debounce.Start(); else RenderingTask = RenderPageAsync();
    }

    private async Task RenderPageAsync()
    {
        var session = _session; if (session is null) return;
        var page = _page; var version = _version; var renderVersion = ++_renderVersion;
        _rendering?.Cancel(); _rendering?.Dispose(); _rendering = new(); var token = _rendering.Token;
        bool Current() => version == _version && renderVersion == _renderVersion && !token.IsCancellationRequested;
        DocumentStatus.Text = "加载页面中…";
        try
        {
            var size = await session.GetPageSizeAsync(page, token);
            if (!Current()) return;
            var rotated = _rotation % 180 != 0;
            var width = rotated ? size.Height : size.Width; var height = rotated ? size.Width : size.Height;
            var viewportWidth = Math.Max(1, _continuous ? ContinuousArea.ActualWidth - 32 : PageScroll.ViewportWidth > 0 ? PageScroll.ViewportWidth : PageScroll.ActualWidth);
            var viewportHeight = Math.Max(1, _continuous ? ContinuousArea.ActualHeight - 16 : PageScroll.ViewportHeight > 0 ? PageScroll.ViewportHeight : PageScroll.ActualHeight);
            if (_fit != 2) _scale = Math.Clamp(_fit == 1 ? viewportWidth / width : Math.Min(viewportWidth / width, viewportHeight / height), .05, 8);
            PageImage.Width = size.Width * _scale; PageImage.Height = size.Height * _scale;
            PageImage.LayoutTransform = new RotateTransform(_rotation);
            ZoomText.Text = $"{_scale:P0}"; PageBox.Text = PageNumber.ToString();
            PreviousPageButton.IsEnabled = _page > 0; NextPageButton.IsEnabled = _page + 1 < session.PageCount;
            if (_continuous)
            {
                await ContinuousArea.ShowAsync(session, _scale, _rotation, size, page);
                if (Current()) DocumentStatus.Text = $"第 {PageNumber} 页，共 {session.PageCount} 页";
                return;
            }
            var dpi = VisualTreeHelper.GetDpi(this);
            var pixels = PixelSize(size, _scale, dpi.DpiScaleX, dpi.DpiScaleY);
            var bitmap = await session.RenderAsync(page, pixels.Width, pixels.Height, token);
            if (!Current()) return;
            PageImage.Source = bitmap; DocumentStatus.Text = $"第 {PageNumber} 页，共 {session.PageCount} 页";
            _ = PrefetchAsync(session, page, _scale, dpi, token);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        { if (Current()) { DocumentStatus.Text = $"页面渲染失败：{ex.Message}"; Log.Warning(ex, "PDF 页面渲染失败 {Path};页 {Page}", _path, page + 1); } }
    }

    internal static (int Width, int Height) PixelSize(Size size, double scale, double dpiX, double dpiY)
    {
        var w = size.Width * scale * dpiX; var h = size.Height * scale * dpiY;
        var reduction = Math.Min(1, PdfPreviewSession.MaximumEdge / Math.Max(w, h));
        return (Math.Max(1, (int)Math.Ceiling(w * reduction)), Math.Max(1, (int)Math.Ceiling(h * reduction)));
    }

    private static async Task PrefetchAsync(PdfPreviewSession session, int page, double scale, DpiScale dpi, CancellationToken token)
    {
        // Start after current-page presentation; a new request cancels queued prefetch work.
        try
        {
            await Task.Delay(250, token);
            foreach (var adjacent in new[] { page + 1, page - 1 })
            {
                token.ThrowIfCancellationRequested();
                if (adjacent < 0 || adjacent >= session.PageCount) continue;
                var size = await session.GetPageSizeAsync(adjacent, token);
                var pixels = PixelSize(size, scale, dpi.DpiScaleX, dpi.DpiScaleY);
                await session.RenderAsync(adjacent, pixels.Width, pixels.Height, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log.Debug(ex, "PDF 相邻页预取失败"); }
    }

    private void Zoom(double factor)
    {
        if (_session is null) return;
        _fit = 2; var previous = _scale; _scale = Math.Clamp(_scale * factor, .05, 8);
        if (double.IsFinite(PageImage.Width)) PageImage.Width *= _scale / previous;
        if (double.IsFinite(PageImage.Height)) PageImage.Height *= _scale / previous;
        ScheduleRender();
    }
    private void OnPreviousPage(object sender, RoutedEventArgs e) => GoToPage(PageNumber - 1);
    private void OnNextPage(object sender, RoutedEventArgs e) => GoToPage(PageNumber + 1);
    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(1.2);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(1 / 1.2);
    private void OnFitPage(object sender, RoutedEventArgs e) { _fit = 0; ScheduleRender(false); }
    private void OnFitWidth(object sender, RoutedEventArgs e) { _fit = 1; ScheduleRender(false); }
    private void OnRotate(object sender, RoutedEventArgs e) { _rotation = (_rotation + 90) % 360; ScheduleRender(false); }
    private void OnViewMode(object sender, RoutedEventArgs e)
    {
        _continuous = !_continuous;
        ViewModeButton.Content = _continuous ? "连续阅读" : "单页";
        PageScroll.Visibility = _continuous ? Visibility.Collapsed : Visibility.Visible;
        ContinuousArea.Visibility = _continuous ? Visibility.Visible : Visibility.Collapsed;
        if (!_continuous) ContinuousArea.Clear();
        _fit = _continuous ? 1 : 0;
        UpdateLayout(); ScheduleRender(false);
    }
    private void OnViewportChanged(object sender, SizeChangedEventArgs e) { if (_fit != 2) ScheduleRender(); }
    private void SubmitPage()
    { if (int.TryParse(PageBox.Text, out var page)) GoToPage(page); else { DocumentStatus.Text = "请输入有效页码。"; PageBox.Text = PageNumber.ToString(); } }
    private void OnPageKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; SubmitPage(); } }
    private void OnPageFocusLost(object sender, KeyboardFocusChangedEventArgs e) => SubmitPage();
    private void OnPageWheel(object sender, MouseWheelEventArgs e)
    { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { Zoom(e.Delta > 0 ? 1.2 : 1 / 1.2); e.Handled = true; } }
    private async void OnPassword(object sender, RoutedEventArgs e)
    {
        var version = _version; var password = await PdfPasswordDialog.ShowAsync();
        if (password is not null && version == _version) { LoadingTask = OpenAsync(password); await LoadingTask; }
    }
    private void OnExternalOpen(object sender, RoutedEventArgs e)
    {
        if (_path is null) return;
        try { Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true }); }
        catch (Exception ex) { DocumentStatus.Text = $"无法打开 PDF：{ex.Message}"; }
    }
}
