using NexusExplorer.Views.Dialogs;
using NexusExplorer.ApplicationLayer;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using Serilog;
using MaterialDesignThemes.Wpf;

namespace NexusExplorer.Views;

public partial class PlayerPanel : UserControl
{
    private MainViewModel _main = null!;
    private PlayerViewModel Vm => _main.Player;
    private IMediaPlaybackControls? Controls => Vm.Engine as IMediaPlaybackControls;
    private IPlaybackLifetime? Lifetime => Vm.Engine as IPlaybackLifetime;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _dragging;
    private bool _initialized;
    private bool _syncingVolume;
    private bool _panning;
    private bool _dialogCovered;
    internal void SetDialogCovered(bool covered)
    {
        _dialogCovered = covered;
        if (covered) VolumePopup.IsPopupOpen = false;
        UpdateUi();
    }
    private Point _panStart;
    private double _effectiveScale = 1;
    public PlayerPanel()
    {
        InitializeComponent();
        VolumeSlider.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler(OnVolumeSliderLostCapture), true);
        VolumePopup.Closed += OnVolumePopupClosed;
        VideoView.SurfaceCreated += hwnd => { if (_main is not null) Lifetime?.AttachSurface(hwnd); };
    }
    private void OnVolumeSliderLostCapture(object sender, MouseEventArgs e)
    {
        // PopupBox's StaysOpen handler otherwise recaptures the entire subtree when
        // Thumb releases capture. Hover volume is not a modal input surface.
        if (e.OriginalSource is Thumb thumb && ReferenceEquals(CategoryFilePanel.FindAncestor<Slider>(thumb), VolumeSlider))
            e.Handled = true;
    }
    private void OnVolumePopupClosed(object sender, RoutedEventArgs e)
    {
        // MouseOver PopupBox does not release capture in its close callback.
        // Only release this popup/slider's capture, never another control's drag.
        if (ReferenceEquals(Mouse.Captured, VolumePopup)
            || Mouse.Captured is DependencyObject captured && ReferenceEquals(CategoryFilePanel.FindAncestor<Slider>(captured), VolumeSlider))
            Mouse.Capture(null);
    }
    public void Initialize(MainViewModel main)
    {
        _main = main; Vm.ShowErrorAsync = async m => { await MessageDialog.ShowAsync(m, "播放", DialogButtons.Ok, DialogSeverity.Warning); };
        Vm.StateChanged += UpdateUi;
        if (Controls is not null) Controls.VideoClicked += OnNativeVideoClick;
        HardwareCheck.IsChecked = Controls?.HardwareDecoding ?? true; VolumeSlider.Value = Vm.Volume; UpdateVolumeUi(Vm.Volume); _initialized = true;
        ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => _dragging = true), true);
        AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnProgressReleased), true);
        _timer.Tick += Poll; _timer.Start();
        Loaded += OnLoaded; Unloaded += OnUnloaded; UpdateUi();
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    { if (Lifetime is not null) await RunAsync(async () => { Lifetime.AttachSurface(VideoView.Handle); await Lifetime.InitializeAsync(); }); }
    private async void OnNativeVideoClick()
    { if (!_dialogCovered && Vm.Kind == MediaKind.Video) await RunAsync(Vm.TogglePlayPauseAsync); }
    public void Detach()
    {
        VolumePopup.IsPopupOpen = false;
        _timer.Stop(); Lifetime?.AttachSurface(IntPtr.Zero);
        if (_initialized) Vm.ReleaseImagePreview();
        ImageDisplay.Source = null;
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach(); Vm.StateChanged -= UpdateUi;
        if (Controls is not null) Controls.VideoClicked -= OnNativeVideoClick;
        _timer.Tick -= Poll; Loaded -= OnLoaded; Unloaded -= OnUnloaded;
    }
    private async Task RunAsync(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Log.Error(ex, "播放控制失败"); await MessageDialog.ShowAsync(ex.Message, "播放", severity: DialogSeverity.Warning); } }
    private void Poll(object? sender, EventArgs e)
        => RefreshPlaybackUi();

    internal void RefreshPlaybackUi()
    {
        if (Vm.Kind is not (MediaKind.Video or MediaKind.Audio)) return;
        var state = Vm.Engine.Snapshot;
        if (Controls is { } controls)
            HardwareCheck.ToolTip = !controls.HardwareDecoding ? "硬件解码：已关闭；下次打开媒体生效"
                : controls.ActiveHardwareDecoder is null ? "硬件解码：已请求开启；等待媒体就绪"
                : controls.ActiveHardwareDecoder == "no" ? "硬件解码：已请求开启，但当前媒体使用软件解码"
                : $"硬件解码：已开启（{controls.ActiveHardwareDecoder}）；修改后下次打开媒体生效";
        if (!Vm.PlaybackCompleted) { Vm.IsPlaying = state.IsPlaying; Vm.Duration = state.Duration; Vm.Position = state.Position; }
        PlayPauseIcon.Kind = Vm.PlaybackCompleted ? PackIconKind.Replay : state.IsPlaying ? PackIconKind.Pause : PackIconKind.Play;
        PlayPauseButton.ToolTip = Vm.PlaybackCompleted ? "重播" : state.IsPlaying ? "暂停" : "播放";
        ProgressSlider.IsEnabled = !Vm.PlaybackCompleted && !state.IsDurationPending && Vm.Duration > TimeSpan.Zero;
        if (_dragging) return;
        ProgressSlider.Value = Vm.Duration > TimeSpan.Zero ? Math.Clamp(Vm.Position.TotalMilliseconds / Vm.Duration.TotalMilliseconds, 0, 1) : 0;
        PositionText.Text = FormatTime(Vm.Position);
        var estimated = Vm.PlaybackCompleted ? Vm.CompletedDurationEstimated : state.IsDurationEstimated;
        DurationText.Text = state.IsDurationPending && !Vm.PlaybackCompleted ? "读取时长中…"
            : (Vm.Duration > TimeSpan.Zero ? (estimated ? "约 " : "") + FormatTime(Vm.Duration) : "未知时长")
                + (Vm.PlaybackCompleted ? " · 已播放完" : "");
    }
    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}" : $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
    private void UpdateUi()
    {
        var media = Vm.Kind is MediaKind.Video or MediaKind.Audio;
        // Material dialogs use their own popup HWND above the native video.
        // Keep rendering underneath; _dialogCovered still blocks video clicks.
        VideoView.Visibility = Vm.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Hidden;
        ImageScroll.Visibility = Vm.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        AudioLayer.Visibility = Vm.Kind == MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
        AudioTitle.Text = Vm.MediaTitle ?? "";
        ControlsBar.Visibility = media || Vm.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        ProgressRow.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        MediaButtonsRow.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        MediaSettingsRow.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        if (!media) VolumePopup.IsPopupOpen = false;
        ImageButtonsRow.Visibility = Vm.Kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        HardwareCheck.Visibility = Vm.Kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;
        ModeButton.ToolTip = Vm.PlayMode switch { PlayMode.RepeatAll => "列表循环", PlayMode.RepeatOne => "单曲循环", PlayMode.Shuffle => "随机", _ => "顺序" };
        ModeIcon.Kind = Vm.PlayMode switch { PlayMode.RepeatAll => PackIconKind.Repeat, PlayMode.RepeatOne => PackIconKind.RepeatOnce, PlayMode.Shuffle => PackIconKind.ShuffleVariant, _ => PackIconKind.PlaylistPlay };
        if (Vm.Kind == MediaKind.Image) { ImageDisplay.Source = Vm.ImageSource; ApplyImageZoom(); }
        else ImageDisplay.Source = null;
        RefreshPlaybackUi();
    }
    private async void OnPlayPause(object sender, RoutedEventArgs e) => await RunAsync(Vm.TogglePlayPauseAsync);
    private async void OnVideoAreaClick(object sender, MouseButtonEventArgs e)
    { if (!_dialogCovered) await RunAsync(Vm.TogglePlayPauseAsync); }
    private async void OnStop(object sender, RoutedEventArgs e) => await RunAsync(Vm.StopPlaybackAsync);
    private async void OnPrevious(object sender, RoutedEventArgs e) => await RunAsync(Vm.PreviousAsync);
    private async void OnNext(object sender, RoutedEventArgs e) => await RunAsync(Vm.NextAsync);
    private void OnCycleMode(object sender, RoutedEventArgs e) => Vm.CyclePlayMode();
    private async void OnProgressReleased(object sender, MouseButtonEventArgs e)
    { if (!_dragging) return; _dragging = false; await RunAsync(() => Vm.SeekAsync((float)ProgressSlider.Value)); }
    private void OnProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_dragging) PositionText.Text = FormatTime(Vm.Duration * e.NewValue); }
    private async void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { UpdateVolumeUi((int)e.NewValue); if (_initialized && !_syncingVolume) await RunAsync(() => Vm.SetVolumeAsync((int)e.NewValue)); }
    private void OnVolumeToggleButtonClick(object sender, RoutedEventArgs e)
    {
        // MouseOver PopupBox consumes mouse-up and raises ToggleCheckedContentClick;
        // keyboard activation (or clicking before hover opens) uses ButtonBase.Click instead.
        if (ReferenceEquals(e.OriginalSource, VolumePopup.Template.FindName(PopupBox.TogglePartName, VolumePopup)))
            OnVolumeToggleClick(sender, e);
    }
    private async void OnVolumeToggleClick(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        await RunAsync(async () =>
        {
            var change = Vm.ToggleVolumeAsync();
            _syncingVolume = true;
            try { VolumeSlider.Value = Vm.Volume; UpdateVolumeUi(Vm.Volume); }
            finally { _syncingVolume = false; }
            await change;
        });
    }
    private void UpdateVolumeUi(int volume)
    {
        if (VolumeIcon is null || VolumeValueText is null) return;
        VolumeIcon.Kind = volume == 0 ? PackIconKind.VolumeMute : volume <= 30 ? PackIconKind.VolumeLow : volume <= 60 ? PackIconKind.VolumeMedium : PackIconKind.VolumeHigh;
        VolumeValueText.Text = volume.ToString(); VolumePopup.ToolTip = $"音量 {volume}%";
    }
    private async void OnHardwareChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || Controls is null) return;
        Controls.HardwareDecoding = HardwareCheck.IsChecked == true;
        try
        {
            var config = Infrastructure.AppConfig.LoadOrDefault(Infrastructure.AppPaths.SettingsPath);
            config.Playback.HardwareDecoding = Controls.HardwareDecoding;
            config.Save(Infrastructure.AppPaths.SettingsPath);
        }
        catch (Exception ex) { Log.Error(ex, "保存播放配置失败"); await MessageDialog.ShowAsync(ex.Message, "播放", severity: DialogSeverity.Warning); }
    }
    private async void OnAudioTracks(object sender, RoutedEventArgs e)
    {
        var controls = Controls; if (controls is null) return;
        var token = controls.CurrentToken;
        var tracks = controls.AudioTracks;
        if (tracks.Count == 0) { await MessageDialog.ShowAsync("无可用音轨", "音轨"); return; }
        var choices = new List<DialogChoice<long>> { new("禁用音轨", -1) };
        choices.AddRange(tracks.Select(t => new DialogChoice<long>(t.Name, t.Id)));
        var selected = await ChoiceDialog.ShowAsync("选择音轨", "音轨", choices, controls.SelectedAudioTrack);
        if (selected.Confirmed && token == controls.CurrentToken) await RunAsync(() => controls.SetAudioTrackAsync(selected.Value, token));
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
        ZoomResetButton.ToolTip = Vm.ImageScale == 0 ? "适合窗口" : $"当前缩放 {_effectiveScale:0.##}×；点击适合窗口";
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
