using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using NexusExplorer.Infrastructure;
using Serilog;

namespace NexusExplorer.Views;

public partial class MainWindow
{
    private bool _focusPreview, _fullscreenTransitioning;
    private sealed record WindowRestoreState(WindowAppearance.Placement Placement, WindowAppearance.RectInt Monitor, WindowStyle Style,
        ResizeMode Resize, bool Topmost, Visibility Library, WindowChrome Chrome);
    private WindowRestoreState? _windowRestore;
    internal bool IsFullscreenTransitioning => _fullscreenTransitioning;
    private void UpdateShellInset()
    {
        if (_fullscreenTransitioning) return;
        if (_focusPreview) WindowSurface.Margin = new Thickness(0);
        else WindowAppearance.UpdateMaximizedInset(this, WindowSurface);
    }

    private async void ToggleFocusPreview()
    {
        try { await ToggleFocusPreviewAsync(); }
        catch (Exception ex) { Log.Error(ex, "全屏切换失败"); }
    }

    internal async Task ToggleFocusPreviewAsync()
    {
        if (_fullscreenTransitioning || !IsVisible) return;
        _fullscreenTransitioning = true;
        var before = _focusPreview;
        var clock = Stopwatch.StartNew();
        try
        {
            // Cover first: native geometry changes must never expose intermediate layouts.
            using var curtain = FullscreenCurtain.Create(this);
            await curtain.PresentAsync();
            if (!IsVisible) return;
            if (!before)
                _windowRestore = new(WindowAppearance.CapturePlacement(this), WindowAppearance.GetMonitorBounds(this),
                    WindowStyle, ResizeMode, Topmost, LibraryCard.Visibility,
                    (WindowChrome)WindowChrome.GetWindowChrome(this).CloneCurrentValue());
            PlayerArea.SetFullscreenTransitioning(true);
            WindowAppearance.SetTransitionsDisabled(this, true);
            try
            {
                ApplyFullscreenState(!before);
                UpdateLayout();
                await curtain.PresentAsync();
            }
            catch
            {
                if (IsVisible) { ApplyFullscreenState(before); UpdateLayout(); }
                throw;
            }
            finally
            {
                WindowAppearance.SetTransitionsDisabled(this, false);
                PlayerArea.SetFullscreenTransitioning(false);
            }
            Log.Information("全屏切换完成;全屏 {Fullscreen};耗时 {ElapsedMs:F1} ms", _focusPreview, clock.Elapsed.TotalMilliseconds);
        }
        finally { _fullscreenTransitioning = false; }
    }

    private void ApplyFullscreenState(bool fullscreen)
    {
        var restore = _windowRestore ?? throw new InvalidOperationException("缺少全屏恢复状态");
        PrismMotion.Stop(this, DrawerExtentProperty, finish: true);
        PrismMotion.Stop(this, LibraryHeightProperty, finish: true);
        PrismMotion.StopAll(LibraryScale, finish: true);
        PrismMotion.StopAll(LibraryTranslate, finish: true);
        PrismMotion.StopAll(LibraryCard, finish: true);
        _focusPreview = fullscreen;
        // Prepare one final layout before changing the native window geometry.
        var center = (Grid)PlayerArea.Parent;
        WindowLayout.Margin = new Thickness(fullscreen ? 0 : 6);
        Workspace.Margin = fullscreen ? new Thickness(0) : new Thickness(20, 8, -6, 16);
        WallpaperScene.Visibility = fullscreen ? Visibility.Hidden : Visibility.Visible;
        TitleBar.Visibility = SearchCard.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        WindowLayout.RowDefinitions[0].Height = new GridLength(fullscreen ? 0 : 64);
        LibraryCard.Visibility = fullscreen ? Visibility.Collapsed : restore.Library;
        LibrarySlot.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        center.RowDefinitions[0].Height = new GridLength(fullscreen ? 0 : 64);
        ((FrameworkElement)NavBar.Parent).Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        PlayerArea.SetFocusPreview(fullscreen);

        if (fullscreen)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            var chrome = (WindowChrome)restore.Chrome.CloneCurrentValue();
            chrome.CaptionHeight = 0; chrome.ResizeBorderThickness = new Thickness(0);
            WindowChrome.SetWindowChrome(this, chrome);
            WindowSurface.Margin = new Thickness(0); Topmost = true;
            WindowAppearance.FillMonitor(this, restore.Monitor);
        }
        else
        {
            WindowStyle = restore.Style; ResizeMode = restore.Resize; Topmost = restore.Topmost;
            WindowChrome.SetWindowChrome(this, (WindowChrome)restore.Chrome.CloneCurrentValue());
            WindowAppearance.RestorePlacement(this, restore.Placement);
            WindowAppearance.UpdateMaximizedInset(this, WindowSurface);
        }
        WindowAppearance.SetFullscreenFrame(this, fullscreen);
    }
}
