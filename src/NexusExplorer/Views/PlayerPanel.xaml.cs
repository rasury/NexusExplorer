using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Views;

/// <summary>
/// 右侧播放面板:视频(VLC 回调帧)/ 音频 / 图片。未播放时纯黑。
/// 视频通过 WriteableBitmap 显示为普通 WPF 元素,不存在原生窗口遮挡问题。
/// </summary>
public partial class PlayerPanel : UserControl
{
    private MainViewModel _main = null!;
    private PlayerViewModel Vm => _main.Player;

    private bool _isDraggingProgress;   // 按下到松开之间(点按同样置位)
    private bool _updatingFromPoll;
    private bool _suppressProgressEvents; // 程序设值(轮询)时抑制

    // 沉浸模式:2 秒无操作隐藏控制条
    private System.Windows.Threading.DispatcherTimer? _immersiveTimer;
    private bool _controlsVisible = true;
    private bool _pointerOverControls;
    private Point _lastMousePosition;
    private bool _hasMousePosition;
    private const double HideDelaySeconds = 2.0;

    // UI 线程轮询 VLC 状态(位置/时长/播放中)
    private System.Windows.Threading.DispatcherTimer? _uiTimer;

    public PlayerPanel()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel main)
    {
        _main = main;
        Vm.ShowError = message =>
            MessageBox.Show(message, "播放", MessageBoxButton.OK, MessageBoxImage.Warning);
        Vm.StateChanged += UpdateUi;

        // VLC 错误事件来自后台线程
        Vm.MediaPlayer.PlaybackError += (path, message) =>
            Dispatcher.Invoke(() => Vm.OnPlaybackError(path, message));

        // 视频位图尺寸变化(新文件/停止)
        Vm.MediaPlayer.VideoSizeChanged += () => Dispatcher.BeginInvoke(UpdateVideoImage);

        // 音量条初始位置与 ViewModel 同步(Volume 默认 80;
        // VLC 未就绪时 SetVolume 只记值不回写,见 PlayerViewModel)
        _volumeInitialized = false;
        VolumeSlider.Value = Vm.Volume;
        _volumeInitialized = true;

        // Slider 的 MoveToPoint 类处理会把 Preview 事件标记为已处理,
        // XAML/普通订阅收不到;必须 handledEventsToo: true 才能接管按下/松开
        ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnProgressDragStart), true);
        ProgressSlider.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnProgressDragEnd), true);
        // 面板级兜底:拖出 Slider 后松开
        AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnPanelPreviewMouseLeftButtonUp), true);

        _uiTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _uiTimer.Tick += (_, _) => PollVlcState();
        _uiTimer.Start();

        // 沉浸模式:鼠标静止 2 秒隐藏控制条
        _immersiveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(HideDelaySeconds)
        };
        _immersiveTimer.Tick += (_, _) => HideControlsForImmersive();

        // 仅当鼠标位置真正移动时才算"用户活动"
        // (WPF 在静止时也会派发少量 MouseMove,直接重启计时器会导致永不隐藏)
        MouseMove += (_, e) =>
        {
            var position = e.GetPosition(this);
            if (!_hasMousePosition)
            {
                _lastMousePosition = position;
                _hasMousePosition = true;
                return;
            }
            if (Math.Abs(position.X - _lastMousePosition.X) > 1
                || Math.Abs(position.Y - _lastMousePosition.Y) > 1)
            {
                _lastMousePosition = position;
                ShowControls();
            }
        };
        ControlsBar.MouseEnter += (_, _) => _pointerOverControls = true;
        ControlsBar.MouseLeave += (_, _) =>
        {
            _pointerOverControls = false;
            RestartImmersiveTimer();
        };
        ProgressSlider.AddHandler(MouseMoveEvent, new MouseEventHandler((_, _) => RestartImmersiveTimer()), true);

        UpdateUi();
    }

    // ---------- 沉浸模式 ----------

    /// <summary>显示控制条并重新计时 2 秒隐藏。</summary>
    private void ShowControls()
    {
        if (!_controlsVisible)
        {
            _controlsVisible = true;
            ControlsBar.IsHitTestVisible = true;
            ControlsBar.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        }
        RestartImmersiveTimer();
    }

    private void HideControlsForImmersive()
    {
        // 鼠标悬停在控制条上/正在拖进度条时不隐藏
        if (_pointerOverControls || _isDraggingProgress)
        {
            RestartImmersiveTimer();
            return;
        }

        // 播放中才进入沉浸(暂停时保持控件可见)
        if (Vm.Kind is not (MediaKind.Video or MediaKind.Audio) || !Vm.IsPlaying)
            return;

        _controlsVisible = false;
        ControlsBar.IsHitTestVisible = false; // 隐藏后点击穿透到视频画面
        ControlsBar.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
    }

    private void RestartImmersiveTimer()
    {
        _immersiveTimer?.Stop();
        _immersiveTimer?.Start();
    }

    private void UpdateVideoImage()
    {
        var bitmap = Vm.MediaPlayer.VideoBitmap;
        VideoImage.Source = bitmap;
        VideoImage.Visibility = bitmap is not null && Vm.Kind == MediaKind.Video
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PollVlcState()
    {
        if (Vm.Kind is not (MediaKind.Video or MediaKind.Audio)) return;

        // VLC 冷启动初始化中(后台预热)→ 本轮跳过,不阻塞 UI 线程
        if (!Vm.MediaPlayer.IsVlcReady) return;
        var player = Vm.MediaPlayer.Player;

        // 播放状态同步(VLC 暂停/播完不会主动通知 UI)
        var isPlaying = player.IsPlaying;
        if (isPlaying != Vm.IsPlaying)
        {
            Vm.IsPlaying = isPlaying;
            PlayPauseButton.Content = isPlaying ? "⏸ 暂停" : "▶ 播放";
            if (!isPlaying)
            {
                // 暂停/停止:立即显示控制条并停止沉浸计时
                _immersiveTimer?.Stop();
                _controlsVisible = true;
                ControlsBar.IsHitTestVisible = true;
                ControlsBar.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            }
            else
            {
                RestartImmersiveTimer();
            }
        }

        // 停止/出错/未加载媒体时不清进度;暂停时仍同步位置显示
        var state = player.State;
        if (state is LibVLCSharp.Shared.VLCState.NothingSpecial
            or LibVLCSharp.Shared.VLCState.Stopped
            or LibVLCSharp.Shared.VLCState.Error)
            return;

        if (_isDraggingProgress || _updatingFromPoll) return;

        _updatingFromPoll = true;
        _suppressProgressEvents = true;
        try
        {
            var length = TimeSpan.FromMilliseconds(player.Length);
            if (length > TimeSpan.Zero)
            {
                var position = player.Position;
                if (position >= 0 && !float.IsNaN(position))
                {
                    ProgressSlider.Value = Math.Clamp(position, 0.0, 1.0);
                    PositionText.Text = FormatTime(TimeSpan.FromMilliseconds(player.Time));
                    DurationText.Text = FormatTime(length);
                    Vm.Duration = length;
                }
            }
        }
        finally
        {
            _updatingFromPoll = false;
            _suppressProgressEvents = false;
        }
    }

    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    // ---------- UI 状态 ----------

    private void UpdateUi()
    {
        switch (Vm.Kind)
        {
            case MediaKind.None:
                VideoImage.Visibility = Visibility.Collapsed;
                ImageScroll.Visibility = Visibility.Collapsed;
                AudioLayer.Visibility = Visibility.Collapsed;
                ControlsBar.Visibility = Visibility.Collapsed;
                _immersiveTimer?.Stop();
                break;

            case MediaKind.Video:
                UpdateVideoImage();
                ImageScroll.Visibility = Visibility.Collapsed;
                AudioLayer.Visibility = Visibility.Collapsed;
                ControlsBar.Visibility = Visibility.Visible;
                ProgressRow.Visibility = Visibility.Visible;
                MediaButtonsRow.Visibility = Visibility.Visible;
                ImageButtonsRow.Visibility = Visibility.Collapsed;
                ModeButton.Visibility = Visibility.Collapsed;
                VolumePanel.Visibility = Visibility.Visible;
                ShowControls(); // 开始播放即计时,2 秒后隐藏进入沉浸
                break;

            case MediaKind.Audio:
                VideoImage.Visibility = Visibility.Collapsed;
                ImageScroll.Visibility = Visibility.Collapsed;
                AudioLayer.Visibility = Visibility.Visible;
                AudioTitle.Text = Vm.MediaTitle ?? string.Empty;
                ControlsBar.Visibility = Visibility.Visible;
                ProgressRow.Visibility = Visibility.Visible;
                MediaButtonsRow.Visibility = Visibility.Visible;
                ImageButtonsRow.Visibility = Visibility.Collapsed;
                ModeButton.Visibility = Visibility.Visible;
                VolumePanel.Visibility = Visibility.Visible;
                ShowControls();
                break;

            case MediaKind.Image:
                // 图片模式:专用控件(上一张/下一张/缩放),隐藏进度与媒体控件
                VideoImage.Visibility = Visibility.Collapsed;
                ImageScroll.Visibility = Visibility.Visible;
                AudioLayer.Visibility = Visibility.Collapsed;
                ControlsBar.Visibility = Visibility.Visible;
                ProgressRow.Visibility = Visibility.Collapsed;
                MediaButtonsRow.Visibility = Visibility.Collapsed;
                ImageButtonsRow.Visibility = Visibility.Visible;
                ModeButton.Visibility = Visibility.Collapsed;
                VolumePanel.Visibility = Visibility.Collapsed;
                ImageDisplay.Source = Vm.ImageSource;
                ApplyImageZoom();
                break;

            case MediaKind.Unsupported:
                VideoImage.Visibility = Visibility.Collapsed;
                ImageScroll.Visibility = Visibility.Collapsed;
                AudioLayer.Visibility = Visibility.Collapsed;
                ControlsBar.Visibility = Visibility.Collapsed;
                _immersiveTimer?.Stop();
                break;
        }

        PlayPauseButton.Content = Vm.IsPlaying ? "⏸ 暂停" : "▶ 播放";
        ModeButton.Content = Vm.PlayMode switch
        {
            PlayMode.RepeatAll => "🔁 列表循环",
            PlayMode.RepeatOne => "🔂 单曲循环",
            PlayMode.Shuffle => "🔀 随机",
            _ => "➡ 顺序"
        };
    }

    // ---------- 控制 ----------

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (Vm.Kind == MediaKind.Image)
        {
            Vm.Next();
            return;
        }
        Vm.TogglePlayPause();
    }

    /// <summary>点击视频/音频画面 → 切换播放/暂停(并在暂停时确保控件可见)。</summary>
    private void OnVideoAreaClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.Kind is not (MediaKind.Video or MediaKind.Audio)) return;
        Vm.TogglePlayPause();
        // 状态切换后由 PollVlcState 的播放状态同步统一处理控制条显隐
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        Vm.StopPlayback();
        ProgressSlider.Value = 0;
        PositionText.Text = "0:00";
        DurationText.Text = "0:00";
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => Vm.Previous();

    private void OnNext(object sender, RoutedEventArgs e) => Vm.Next();

    private void OnCycleMode(object sender, RoutedEventArgs e) => Vm.CyclePlayMode();

    private void OnProgressDragStart(object sender, MouseButtonEventArgs e) => _isDraggingProgress = true;

    private void OnProgressDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingProgress)
        {
            _isDraggingProgress = false;
            // 点按(IsMoveToPointEnabled 已把 Value 设为点击位置)与拖拽释放都走这里
            Vm.Seek((float)ProgressSlider.Value);
        }
    }

    /// <summary>松开事件兜底:面板级接管(拖出进度条边界松开也能完成 seek)。</summary>
    private void OnPanelPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingProgress)
        {
            _isDraggingProgress = false;
            Vm.Seek((float)ProgressSlider.Value);
        }
    }

    private void OnProgressChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 拖拽/点按期间实时更新预览时间(轮询此时已暂停,不会覆盖)
        if (_isDraggingProgress && Vm.Duration > TimeSpan.Zero)
        {
            PositionText.Text = FormatTime(Vm.Duration * e.NewValue);
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_main is null) return;
        // 初始化同步(非用户操作)时只对齐位置,不触发回写
        if (!_volumeInitialized)
        {
            _volumeInitialized = true;
            return;
        }
        Vm.SetVolume((int)e.NewValue);
    }

    private bool _volumeInitialized;

    // ---------- 图片:缩放与平移 ----------

    // 拖拽平移状态
    private bool _isPanning;
    private Point _panStartPoint;
    private const double MinImageScale = 0.05;
    private const double MaxImageScale = 20.0;

    /// <summary>滚轮缩放:以鼠标位置为不动点(缩放中心跟随光标)。</summary>
    private void OnImageWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm.Kind != MediaKind.Image) return;

        var position = e.GetPosition(ImageScroll);
        var factor = e.Delta > 0 ? 1.2 : 1 / 1.2;
        ZoomAt(position, factor);
        e.Handled = true;
    }

    /// <summary>缩放并保持 scroll 视口内锚点位置。</summary>
    private void ZoomAt(Point viewportAnchor, double factor)
    {
        var oldScale = Vm.ImageScale;
        var newScale = Math.Clamp(oldScale * factor, MinImageScale, MaxImageScale);
        if (Math.Abs(newScale - oldScale) < 0.0001) return;
        var actualFactor = newScale / oldScale;

        // 记录锚点处的偏移(缩放前)
        var offsetX = ImageScroll.HorizontalOffset;
        var offsetY = ImageScroll.VerticalOffset;
        var anchorX = offsetX + viewportAnchor.X;
        var anchorY = offsetY + viewportAnchor.Y;

        Vm.ImageScale = newScale;
        ApplyImageZoom();
        ImageScroll.UpdateLayout();

        // 锚点保持:新的偏移 = 锚点内容坐标 × 实际缩放比 − 视口锚点
        ImageScroll.ScrollToHorizontalOffset(anchorX * actualFactor - viewportAnchor.X);
        ImageScroll.ScrollToVerticalOffset(anchorY * actualFactor - viewportAnchor.Y);
    }

    private void OnZoomIn(object sender, RoutedEventArgs e)
    {
        var center = new Point(ImageScroll.ViewportWidth / 2, ImageScroll.ViewportHeight / 2);
        ZoomAt(center, 1.4);
    }

    private void OnZoomOut(object sender, RoutedEventArgs e)
    {
        var center = new Point(ImageScroll.ViewportWidth / 2, ImageScroll.ViewportHeight / 2);
        ZoomAt(center, 1 / 1.4);
    }

    /// <summary>适合窗口(默认态)。</summary>
    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        Vm.ResetZoom();
        ApplyImageZoom();
        ImageScroll.ScrollToHome();
    }

    /// <summary>1:1 原始像素尺寸。</summary>
    private void OnZoomOriginal(object sender, RoutedEventArgs e)
    {
        Vm.ResetZoom();
        Vm.ImageScale = 1.0; // 1.0 = 原始尺寸(见 ApplyImageZoom)
        ApplyImageZoom();
        // 居中
        ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(Math.Max(0, (ImageScroll.ScrollableWidth) / 2));
        ImageScroll.ScrollToVerticalOffset(Math.Max(0, (ImageScroll.ScrollableHeight) / 2));
    }

    // 拖拽平移
    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm.Kind != MediaKind.Image) return;
        _isPanning = true;
        _panStartPoint = e.GetPosition(ImageScroll);
        ImageDisplay.CaptureMouse();
    }

    private void OnImageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var position = e.GetPosition(ImageScroll);
        var dx = position.X - _panStartPoint.X;
        var dy = position.Y - _panStartPoint.Y;
        ImageScroll.ScrollToHorizontalOffset(ImageScroll.HorizontalOffset - dx);
        ImageScroll.ScrollToVerticalOffset(ImageScroll.VerticalOffset - dy);
        _panStartPoint = position;
    }

    private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        ImageDisplay.ReleaseMouseCapture();
    }

    /// <summary>
    /// 应用缩放:0 = Fit 窗口(Uniform);其余值 = 缩放系数(1.0 = 原始像素)。
    /// </summary>
    private void ApplyImageZoom()
    {
        if (Vm.ImageScale is < 0.001)
        {
            // Fit 模式
            ImageDisplay.Stretch = Stretch.Uniform;
            ImageDisplay.LayoutTransform = null;
            ZoomResetButton.Content = "适合窗口";
        }
        else
        {
            // 实际尺寸 = Fit 尺寸 × scale 不直观;直接用原始像素:
            // Stretch=None + ScaleTransform(scale)
            ImageDisplay.Stretch = Stretch.None;
            ImageDisplay.LayoutTransform = new ScaleTransform(Vm.ImageScale, Vm.ImageScale);
            ZoomResetButton.Content = Vm.ImageScale == 1.0 ? "1:1" : $"{Vm.ImageScale:0.0}×";
        }
    }
}
