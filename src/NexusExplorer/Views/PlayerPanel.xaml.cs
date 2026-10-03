using NexusExplorer.ApplicationLayer;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using Serilog;

namespace NexusExplorer.Views;

public partial class PlayerPanel : UserControl
{
    private MainViewModel _main = null!;
    private PlayerViewModel Vm => _main.Player;
    private MediaPlayerService? Native => Vm.Engine as MediaPlayerService;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _dragging;
    private bool _initialized;
    private bool _panning;
    private Point _panStart;
    private double _effectiveScale = 1;
    private readonly NativeVideoBackground _nativeBackground = new();
    public PlayerPanel()
    {
        InitializeComponent();
        VideoView.Loaded += (_, _) => ApplyNativeBackground();
        VideoView.SizeChanged += (_, _) => ApplyNativeBackground();
    }
    public void Initialize(MainViewModel main)
    {
        _main = main; Vm.ShowError = m => MessageBox.Show(m, "播放", MessageBoxButton.OK, MessageBoxImage.Warning);
        Vm.StateChanged += UpdateUi;
        if (Native is not null) Native.PlayerReady += AttachNativePlayer;
        HardwareCheck.IsChecked = Native?.HardwareDecoding ?? true; VolumeSlider.Value = Vm.Volume; _initialized = true;
        ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => _dragging = true), true);
        AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnProgressReleased), true);
        _timer.Tick += Poll; _timer.Start();
        Loaded += OnLoaded; Unloaded += OnUnloaded; UpdateUi();
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    { if (Native is not null) await RunAsync(async () => { await Native.InitializeAsync(); AttachNativePlayer(); }); }
    private void AttachNativePlayer()
    { VideoView.MediaPlayer = Native?.NativePlayer; ApplyNativeBackground(); }
    private void ApplyNativeBackground() => _nativeBackground.Attach(VideoView.MediaPlayer?.Hwnd ?? IntPtr.Zero);
    public void Detach()
    { _timer.Stop(); _nativeBackground.Dispose(); VideoView.MediaPlayer = null; }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach(); Vm.StateChanged -= UpdateUi;
        if (Native is not null) Native.PlayerReady -= AttachNativePlayer;
        _timer.Tick -= Poll; Loaded -= OnLoaded; Unloaded -= OnUnloaded;
    }
    private async Task RunAsync(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Log.Error(ex, "播放控制失败"); Vm.ShowError?.Invoke(ex.Message); } }
    private void Poll(object? sender, EventArgs e)
    {
        if (Vm.Kind is not (MediaKind.Video or MediaKind.Audio)) return;
        var state = Vm.Engine.Snapshot; Vm.IsPlaying = state.IsPlaying; Vm.Duration = state.Duration; Vm.Position = state.Position;
        PlayPauseButton.Content = state.IsPlaying ? "⏸ 暂停" : "▶ 播放";
        if (_dragging) return;
        ProgressSlider.Value = state.Duration > TimeSpan.Zero ? Math.Clamp(state.Position.TotalMilliseconds / state.Duration.TotalMilliseconds, 0, 1) : 0;
        PositionText.Text = FormatTime(state.Position); DurationText.Text = FormatTime(state.Duration);
    }
    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}" : $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
    private void UpdateUi()
    {
        var media = Vm.Kind is MediaKind.Video or MediaKind.Audio;
        VideoView.Visibility = Vm.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;
        if (Vm.Kind == MediaKind.Video) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)ApplyNativeBackground);
        ImageScroll.Visibility = Vm.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        AudioLayer.Visibility = Vm.Kind == MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
        AudioTitle.Text = Vm.MediaTitle ?? "";
        ControlsBar.Visibility = media || Vm.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        ProgressRow.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        MediaButtonsRow.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        ImageButtonsRow.Visibility = Vm.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        HardwareCheck.Visibility = Vm.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;
        ModeButton.Content = Vm.PlayMode switch { PlayMode.RepeatAll => "🔁 列表循环", PlayMode.RepeatOne => "🔂 单曲循环", PlayMode.Shuffle => "🔀 随机", _ => "➡ 顺序" };
        if (Vm.Kind == MediaKind.Image) { ImageDisplay.Source = Vm.ImageSource; ApplyImageZoom(); }
        else ImageDisplay.Source = null;
    }
    private async void OnPlayPause(object sender, RoutedEventArgs e) => await RunAsync(Vm.TogglePlayPauseAsync);
    private async void OnVideoAreaClick(object sender, MouseButtonEventArgs e) => await RunAsync(Vm.TogglePlayPauseAsync);
    private async void OnStop(object sender, RoutedEventArgs e) => await RunAsync(Vm.StopPlaybackAsync);
    private async void OnPrevious(object sender, RoutedEventArgs e) => await RunAsync(Vm.PreviousAsync);
    private async void OnNext(object sender, RoutedEventArgs e) => await RunAsync(Vm.NextAsync);
    private void OnCycleMode(object sender, RoutedEventArgs e) => Vm.CyclePlayMode();
    private async void OnProgressReleased(object sender, MouseButtonEventArgs e)
    { if (!_dragging) return; _dragging = false; await RunAsync(() => Vm.SeekAsync((float)ProgressSlider.Value)); }
    private void OnProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_dragging) PositionText.Text = FormatTime(Vm.Duration * e.NewValue); }
    private async void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_initialized) await RunAsync(() => Vm.SetVolumeAsync((int)e.NewValue)); }
    private void OnHardwareChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || Native is null) return;
        Native.HardwareDecoding = HardwareCheck.IsChecked == true;
        try
        {
            var config = Infrastructure.AppConfig.LoadOrDefault(Infrastructure.AppPaths.SettingsPath);
            config.Playback.HardwareDecoding = Native.HardwareDecoding;
            config.Save(Infrastructure.AppPaths.SettingsPath);
        }
        catch (Exception ex) { Log.Error(ex, "保存播放配置失败"); Vm.ShowError?.Invoke(ex.Message); }
    }
    private void OnAudioTracks(object sender, RoutedEventArgs e)
    {
        var native = Native?.NativePlayer; if (native is null) return;
        Native!.LogAudioDiagnostics();
        var menu = new ContextMenu();
        foreach (var track in native.AudioTrackDescription)
        {
            var id = track.Id; var item = new MenuItem { Header = track.Name, IsCheckable = true, IsChecked = id == native.AudioTrack };
            item.Click += async (_, _) => await RunAsync(() => Native.SetAudioTrackAsync(id));
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "无可用音轨", IsEnabled = false });
        menu.PlacementTarget = sender as UIElement; menu.IsOpen = true;
    }
    internal static double FitScale(double width, double height, double viewportWidth, double viewportHeight) =>
        width <= 0 || height <= 0 ? 1 : Math.Max(0.001, Math.Min(1, Math.Min(Math.Max(1, viewportWidth) / width, Math.Max(1, viewportHeight) / height)));
    private void OnImageSizeChanged(object sender, SizeChangedEventArgs e) { if (_main is not null && Vm.ImageScale == 0) ApplyImageZoom(); }
    private void ApplyImageZoom()
    {
        if (ImageDisplay.Source is not BitmapSource source) return;
        var w = ImageScroll.ViewportWidth > 0 ? ImageScroll.ViewportWidth : ImageScroll.ActualWidth;
        var h = ImageScroll.ViewportHeight > 0 ? ImageScroll.ViewportHeight : ImageScroll.ActualHeight;
        _effectiveScale = Vm.ImageScale > 0 ? Vm.ImageScale : FitScale(source.PixelWidth, source.PixelHeight, w, h);
        ImageDisplay.Width = source.PixelWidth * _effectiveScale; ImageDisplay.Height = source.PixelHeight * _effectiveScale;
        ZoomResetButton.Content = Vm.ImageScale == 0 ? "适合窗口" : $"{_effectiveScale:0.##}×";
    }
    private void ZoomAt(Point point, double factor)
    {
        if (ImageDisplay.Source is null) return;
        ApplyImageZoom(); var old = _effectiveScale; var next = Math.Clamp(old * factor, 0.001, 20);
        var x = ImageScroll.HorizontalOffset + point.X; var y = ImageScroll.VerticalOffset + point.Y;
        Vm.ImageScale = next; ApplyImageZoom(); ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(x * next / old - point.X); ImageScroll.ScrollToVerticalOffset(y * next / old - point.Y);
    }
    private void OnImageWheel(object sender, MouseWheelEventArgs e)
    { ZoomAt(e.GetPosition(ImageScroll), e.Delta > 0 ? 1.2 : 1 / 1.2); e.Handled = true; }
    private void OnZoomIn(object sender, RoutedEventArgs e) => ZoomAt(new Point(ImageScroll.ViewportWidth / 2, ImageScroll.ViewportHeight / 2), 1.4);
    private void OnZoomOut(object sender, RoutedEventArgs e) => ZoomAt(new Point(ImageScroll.ViewportWidth / 2, ImageScroll.ViewportHeight / 2), 1 / 1.4);
    private void OnZoomReset(object sender, RoutedEventArgs e) { Vm.ResetZoom(); ImageScroll.ScrollToHome(); }
    private void OnZoomOriginal(object sender, RoutedEventArgs e) { Vm.ImageScale = 1; ApplyImageZoom(); }
    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    { _panning = true; _panStart = e.GetPosition(ImageScroll); ImageDisplay.CaptureMouse(); }
    private void OnImageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return; var p = e.GetPosition(ImageScroll);
        ImageScroll.ScrollToHorizontalOffset(ImageScroll.HorizontalOffset - (p.X - _panStart.X));
        ImageScroll.ScrollToVerticalOffset(ImageScroll.VerticalOffset - (p.Y - _panStart.Y)); _panStart = p;
    }
    private void OnImageMouseUp(object sender, MouseButtonEventArgs e) { _panning = false; ImageDisplay.ReleaseMouseCapture(); }
}
