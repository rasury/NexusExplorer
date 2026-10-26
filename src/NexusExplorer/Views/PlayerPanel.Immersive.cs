using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NexusExplorer.Views;

public partial class PlayerPanel
{
    internal static readonly TimeSpan ImmersiveIdleDelay = TimeSpan.FromMilliseconds(1000);
    private readonly DispatcherTimer _immersiveTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    // Popup supplies a separate HWND above mpv. Reuse the real controls, not a second player UI.
    private readonly Popup _fullscreenControls = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Relative };
    private readonly Grid _fullscreenViewport = new() { ClipToBounds = true };
    private readonly Grid _fullscreenMotion = new() { Margin = new Thickness(0, 20, 0, 24) };
    private readonly TranslateTransform _fullscreenSlide = new();
    private double _fullscreenSlideDistance;
    private Point? _lastPointer;
    private long _lastPointerMotion, _lastControlsInteraction;
    private bool _cursorHidden, _controlsShown;
    private bool _fullscreenTransitioning;
    internal void SetFullscreenTransitioning(bool transitioning)
    {
        _fullscreenTransitioning = transitioning;
        ResetImmersivePointer();
        if (transitioning) { VolumePopup.IsPopupOpen = false; ShowFullscreenControls(false, immediate: true); }
        else if (_focusPreview) ShowFullscreenControls(true);
    }
    private Thickness _normalControlsMargin;

    private void InitializeImmersiveControls()
    {
        _fullscreenMotion.RenderTransform = _fullscreenSlide;
        _fullscreenViewport.Children.Add(_fullscreenMotion);
        _immersiveTimer.Tick += (_, _) => TrackImmersivePointer();
        ControlsBar.PreviewKeyDown += (_, e) =>
        {
            if (_focusPreview && e.Key is Key.Escape or Key.F11)
            {
                e.Handled = true;
                ExitFocusPreviewRequested?.Invoke();
            }
        };
        Loaded += (_, _) => { ResetImmersivePointer(); _immersiveTimer.Start(); };
        SizeChanged += (_, _) => PositionFullscreenControls();
    }

    private void SetImmersiveControls(bool fullscreen)
    {
        ResetImmersivePointer();
        VolumePopup.IsPopupOpen = false;
        if (fullscreen)
        {
            _normalControlsMargin = ControlsBar.Margin;
            PlayerLayout.Children.Remove(ControlsBar);
            ControlsBar.Margin = new Thickness(0);
            _fullscreenControls.PlacementTarget = PreviewSurface;
            _fullscreenMotion.Children.Add(ControlsBar);
            _fullscreenControls.Child = _fullscreenViewport;
            PositionFullscreenControls();
            ShowFullscreenControls(true);
        }
        else
        {
            ShowFullscreenControls(false, immediate: true);
            _fullscreenControls.Child = null;
            _fullscreenMotion.Children.Remove(ControlsBar);
            ControlsBar.ClearValue(WidthProperty);
            ControlsBar.Margin = _normalControlsMargin;
            PlayerLayout.Children.Add(ControlsBar);
            ControlsBar.Opacity = 1;
            ControlsBar.IsHitTestVisible = true;
        }
    }

    private void PositionFullscreenControls()
    {
        if (!_focusPreview) return;
        ControlsBar.Width = Math.Max(0, PreviewSurface.ActualWidth - 48);
        ControlsBar.Measure(new Size(ControlsBar.Width, double.PositiveInfinity));
        _fullscreenSlideDistance = ControlsBar.DesiredSize.Height + 24;
        _fullscreenViewport.Width = ControlsBar.Width;
        _fullscreenViewport.Height = ControlsBar.DesiredSize.Height + 44;
        _fullscreenControls.HorizontalOffset = 24;
        _fullscreenControls.VerticalOffset = Math.Max(0, PreviewSurface.ActualHeight - _fullscreenViewport.Height);
    }

    private void ShowFullscreenControls(bool show, bool immediate = false)
    {
        if (show && _fullscreenTransitioning) return;
        if (_controlsShown == show && !immediate) return;
        _controlsShown = show;
        ControlsBar.IsHitTestVisible = show;
        if (show)
        {
            _lastControlsInteraction = Stopwatch.GetTimestamp();
            PositionFullscreenControls();
            if (!_fullscreenControls.IsOpen)
            {
                _fullscreenSlide.Y = _fullscreenSlideDistance;
                ControlsBar.Opacity = 0;
            }
            _fullscreenControls.IsOpen = true;
        }
        if (immediate)
        {
            PrismMotion.Stop(ControlsBar, OpacityProperty);
            PrismMotion.Stop(_fullscreenSlide, TranslateTransform.YProperty);
            _fullscreenSlide.Y = 0;
            _fullscreenControls.IsOpen = false;
            return;
        }
        var motion = show ? MotionProfiles.ImmersiveEnter : MotionProfiles.ImmersiveExit;
        PrismMotion.Animate(ControlsBar, OpacityProperty, show ? 1 : 0, motion, minimum: 0, maximum: 1);
        // Animate only the visual transform inside a fixed HWND; no per-frame popup reposition/layout.
        // The viewport extends to the screen bottom so the outgoing card is clipped naturally.
        PrismMotion.Animate(_fullscreenSlide, TranslateTransform.YProperty, show ? 0 : _fullscreenSlideDistance, motion,
            () => { if (!_controlsShown) _fullscreenControls.IsOpen = false; });
    }

    internal static bool ImmersiveTimeoutElapsed(long start, long now) =>
        Stopwatch.GetElapsedTime(start, now) >= ImmersiveIdleDelay;

    private void TrackImmersivePointer()
    {
        if (_fullscreenTransitioning) return;
        if (!IsVisible || Window.GetWindow(this)?.IsActive != true || _dialogCovered || !GetCursorPos(out var position))
        {
            ResetImmersivePointer();
            if (_focusPreview) ShowFullscreenControls(false);
            return;
        }
        var now = Stopwatch.GetTimestamp();
        var screen = new Point(position.X, position.Y);
        var moved = _lastPointer != screen;
        _lastPointer = screen;
        if (moved) _lastPointerMotion = now;
        var local = PreviewSurface.PointFromScreen(screen);
        var inside = new Rect(PreviewSurface.RenderSize).Contains(local);
        var interacting = ControlsBar.IsMouseOver || VolumePopup.IsPopupOpen || _dragging
            || Mouse.Captured is DependencyObject captured && CategoryFilePanel.FindAncestor<PrismCard>(captured) == ControlsBar;
        if (_focusPreview)
        {
            // Bottom edge is a stable activation target even when the popup is fully hidden.
            if (inside && local.Y >= PreviewSurface.ActualHeight - 88 || interacting)
            {
                _lastControlsInteraction = now;
                ShowFullscreenControls(true);
            }
            else if (ImmersiveTimeoutElapsed(_lastControlsInteraction, now)) ShowFullscreenControls(false);
        }
        SetPreviewCursorHidden(inside && !interacting && !moved && ImmersiveTimeoutElapsed(_lastPointerMotion, now));
    }

    private void SetPreviewCursorHidden(bool hidden)
    {
        if (_cursorHidden != hidden)
        {
            _cursorHidden = hidden;
            if (hidden) { PreviewSurface.Cursor = Cursors.None; PreviewSurface.ForceCursor = true; }
            else { PreviewSurface.ClearValue(CursorProperty); PreviewSurface.ClearValue(ForceCursorProperty); }
        }
        // WPF cursors do not reach mpv's child HWND. Never change the global ShowCursor counter.
        if (GetCursorPos(out var p))
        {
            var window = WindowFromPoint(p);
            if (VideoView.Handle != IntPtr.Zero && (window == VideoView.Handle || IsChild(VideoView.Handle, window)))
                SetCursor(hidden ? IntPtr.Zero : LoadCursor(IntPtr.Zero, (IntPtr)32512));
        }
    }

    private void ResetImmersivePointer()
    {
        _lastPointer = null;
        _lastPointerMotion = _lastControlsInteraction = Stopwatch.GetTimestamp();
        SetPreviewCursorHidden(false);
    }

    private void StopImmersiveControls()
    {
        _immersiveTimer.Stop();
        ResetImmersivePointer();
        ShowFullscreenControls(false, immediate: true);
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
}
