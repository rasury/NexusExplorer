using NexusExplorer.Views.Dialogs;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Infrastructure;
using NexusExplorer.ApplicationLayer;
using MaterialDesignThemes.Wpf;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NexusExplorer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _main;

    public MainWindow(MainViewModel main, CategoryService categoryService,
        FileService fileService, OrganizationService organizationService,
        IRecycleBinService recycleBin, IPlaybackEngine mediaPlayer)
    {
        InitializeComponent();

        _main = main;
        CreateRootCategoryButton.Command = main.Category.CreateRootCommand;

        // 注入面板依赖
        LeftPanel.Initialize(_main, recycleBin);
        PlayerArea.Initialize(_main);
        NavBar.Initialize(_main);
        Activated += (_, _) => { if (_focusPreview) Topmost = true; };
        Deactivated += (_, _) => { if (_focusPreview) Topmost = false; };
        PlayerArea.FocusPreviewRequested += ToggleFocusPreview;
        PlayerArea.ExitFocusPreviewRequested += () => { if (_focusPreview) ToggleFocusPreview(); };
        LeftPanel.PresentationSizeChanged += ResizeLibrary;
        Workspace.SizeChanged += (_, _) => ResizeLibrary();
        Loaded += async (_, _) => { try { await ApplyWallpaperAsync(AppConfig.LoadOrDefault(AppPaths.SettingsPath).Appearance); } catch (Exception ex) { Serilog.Log.Warning(ex, "自定义背景不可用，使用默认背景"); } };
        StateChanged += (_, _) => { Dispatcher.BeginInvoke(() => WindowAppearance.UpdateMaximizedInset(this, WindowSurface)); MaximizeIcon.Kind = WindowState == WindowState.Maximized ? PackIconKind.WindowRestore : PackIconKind.WindowMaximize; WindowLayout.Margin = new Thickness(_focusPreview ? 0 : 6); };
        SizeChanged += (_, _) => WindowAppearance.UpdateMaximizedInset(this, WindowSurface);
        UiThemeService.Changed += OnThemeChanged;
        SourceInitialized += (_, _) => { WindowAppearance.Apply(this); WindowAppearance.UpdateMaximizedInset(this, WindowSurface); };
        Loaded += (_, _) => { WindowAppearance.Apply(this); WindowAppearance.UpdateMaximizedInset(this, WindowSurface); };
        MaterialDialogService.NoticeRequested += OnNotice;
        MaterialDialogService.InputWindowStateChanged += OnInputWindowStateChanged;
        Closed += (_, _) => { StopMotion(); UiThemeService.Changed -= OnThemeChanged; MaterialDialogService.NoticeRequested -= OnNotice; MaterialDialogService.InputWindowStateChanged -= OnInputWindowStateChanged; _noticeTimer.Stop(); PlayerArea.FocusPreviewRequested -= ToggleFocusPreview; };
        RootDialog.Identifier = "Nexus." + Guid.NewGuid().ToString("N");
        RootDialog.Loaded += (_, _) => MaterialDialogService.Register(RootDialog);
        RootDialog.Unloaded += (_, _) => MaterialDialogService.Unregister(RootDialog);
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F11) { ToggleFocusPreview(); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.Escape && _focusPreview) { ToggleFocusPreview(); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.F6 && _main.Player.Engine is IPlaybackDiagnostics diagnostics)
            { e.Handled = true; await diagnostics.RecordDiagnosticsAsync(); }
        };

        Loaded += async (_, _) =>
        {
            try { await _main.RefreshTreeAsync(); await LeftPanel.RestoreBrowseAsync(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "加载界面失败"); await MessageDialog.ShowAsync(ex.Message, "加载失败"); }
        };
        Closing += async (_, e) =>
        {
            if (_closingFinished) return;
            e.Cancel = true;
            if (_closingStarted) return;
            _closingStarted = true; IsEnabled = false;
            MaterialDialogService.CancelAll();
            try
            {
                await LeftPanel.PrepareForCloseAsync(); LeftPanel.SaveUiState();
                if (mediaPlayer is IPlaybackLifetime lifetime) await lifetime.ShutdownAsync();
                else await mediaPlayer.StopAndReleaseAsync();
                PlayerArea.Detach();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "退出时停止播放失败"); _closingStarted = false; IsEnabled = true;
                await MessageDialog.ShowAsync("播放器尚未释放资源，请保留日志并稍后重试关闭。", "关闭失败"); return;
            }
            _closingFinished = true; Close();
        };
    }
    private bool _closingStarted;
    private bool _closingFinished;
    private void OnThemeChanged()
    {
        WindowAppearance.Apply(this);
        DefaultBackground.IsDark = UiThemeService.IsDark;
    }
    private readonly DispatcherTimer _noticeTimer = new() { Interval = MotionProfiles.NoticeHold };
    private int _noticeVersion;
    private void OnNotice(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => OnNotice(message)); return; }
        var version = ++_noticeVersion;
        var wasHidden = NoticeCard.Visibility != Visibility.Visible;
        _noticeTimer.Stop(); NoticeText.Text = message; NoticeCard.Visibility = Visibility.Visible;
        NoticeCard.UpdateLayout();
        if (wasHidden) NoticeTransform.Y = -Math.Max(100, NoticeCard.ActualHeight + 30);
        PrismMotion.Animate(NoticeTransform, TranslateTransform.YProperty, 0, MotionProfiles.NoticeEnter,
            () => { if (version == _noticeVersion) _noticeTimer.Start(); });
        _noticeTimer.Tick -= HideNotice; _noticeTimer.Tick += HideNotice;
    }
    private void HideNotice(object? sender, EventArgs e)
    {
        _noticeTimer.Stop(); var version = _noticeVersion;
        PrismMotion.Animate(NoticeTransform, TranslateTransform.YProperty, -Math.Max(100, NoticeCard.ActualHeight + 30), MotionProfiles.NoticeExit,
            () => { if (version == _noticeVersion) NoticeCard.Visibility = Visibility.Collapsed; });
    }
    // A signed extent preserves spring overshoot beyond the zero-width boundary.
    // The slot consumes positive space; the center stretches into negative space.
    private static readonly DependencyProperty DrawerExtentProperty = DependencyProperty.Register(
        "DrawerExtent", typeof(double), typeof(MainWindow), new PropertyMetadata(320d, (d, e) =>
        {
            var window = (MainWindow)d;
            var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            var extent = Math.Round((double)e.NewValue * dpi) / dpi;
            window.LibrarySlot.Width = Math.Max(0, extent);
            window.CenterWorkspace.Margin = new Thickness(Math.Min(0, extent), 0, 0, 0);
        }));
    private static readonly DependencyProperty LibraryHeightProperty = DependencyProperty.Register(
        "LibraryHeight", typeof(double), typeof(MainWindow), new PropertyMetadata(360d, (d, e) =>
        {
            var window = (MainWindow)d;
            var dpi = VisualTreeHelper.GetDpi(window).DpiScaleY;
            window.LibraryCard.Height = Math.Round((double)e.NewValue * dpi) / dpi;
        }));
    private bool _libraryOpen = true;
    private double _libraryHeight;
    private void ResizeLibrary()
    {
        var available = Math.Max(360, Workspace.ActualHeight - 64);
        LibrarySlot.MaxHeight = available;
        var height = Math.Clamp(LeftPanel.PreferredHeight + 72, 360, available);
        if (Math.Abs(height - _libraryHeight) < 1) return;
        _libraryHeight = height;
        if (double.IsNaN(LibraryCard.Height)) { SetCurrentValue(LibraryHeightProperty, height); LibraryCard.Height = height; }
        else PrismMotion.Animate(this, LibraryHeightProperty, height, MotionProfiles.Panel, minimum: 0);
    }
    private void OnToggleDrawer(object sender, RoutedEventArgs e)
    {
        _libraryOpen = !_libraryOpen;
        LibraryCard.IsHitTestVisible = _libraryOpen;
        PrismMotion.Animate(this, DrawerExtentProperty, _libraryOpen ? 320 : 0, MotionProfiles.Panel);
        PrismMotion.Animate(LibraryScale, ScaleTransform.ScaleXProperty, _libraryOpen ? 1 : .78, MotionProfiles.Panel);
        PrismMotion.Animate(LibraryScale, ScaleTransform.ScaleYProperty, _libraryOpen ? 1 : .78, MotionProfiles.Panel);
        PrismMotion.Animate(LibraryTranslate, TranslateTransform.XProperty, _libraryOpen ? 0 : 160, MotionProfiles.Panel);
        PrismMotion.Animate(LibraryCard, OpacityProperty, _libraryOpen ? 1 : 0, MotionProfiles.Fade, minimum: 0, maximum: 1);
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => LeftPanel?.SetFileFilter(FileSearch.Text, _globalSearch);
    private bool _globalSearch;
    private void OnSearchScopeClick(object sender, RoutedEventArgs e)
    {
        _globalSearch = !_globalSearch;
        SearchScope.ApplyTemplate();
        if (SearchScope.Template.FindName("ScopeTranslate", SearchScope) is System.Windows.Media.TranslateTransform slide)
            PrismMotion.Animate(slide, TranslateTransform.XProperty, _globalSearch ? -104 : 0, MotionProfiles.Scope);
        HintAssist.SetHint(FileSearch, _globalSearch ? "搜索全部分类文件…" : "搜索当前分类文件…");
        if (!string.IsNullOrWhiteSpace(FileSearch.Text)) LeftPanel.SetFileFilter(FileSearch.Text, _globalSearch);
    }
    private bool _focusPreview;
    private WindowState _beforeFocusState;
    private Visibility _beforeFocusLibrary;
    private Rect _beforeFocusBounds;
    private void ToggleFocusPreview()
    {
        PrismMotion.Stop(this, DrawerExtentProperty, finish: true);
        _focusPreview = !_focusPreview;
        var center = (Grid)PlayerArea.Parent;
        if (_focusPreview)
        {
            _beforeFocusState = WindowState; _beforeFocusLibrary = LibraryCard.Visibility;
            _beforeFocusBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowSurface.Margin = new Thickness(0); Topmost = true;
            WindowAppearance.FillMonitor(this);
        }
        else
        {
            Topmost = false; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            Left = _beforeFocusBounds.Left; Top = _beforeFocusBounds.Top; Width = _beforeFocusBounds.Width; Height = _beforeFocusBounds.Height;
            WindowState = _beforeFocusState;
            WindowAppearance.UpdateMaximizedInset(this, WindowSurface);
        }
        WindowLayout.Margin = new Thickness(_focusPreview ? 0 : 6);
        Workspace.Margin = _focusPreview ? new Thickness(0) : new Thickness(20, 8, -6, 16);
        WallpaperScene.Visibility = _focusPreview ? Visibility.Hidden : Visibility.Visible;
        TitleBar.Visibility = SearchCard.Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Shell.WindowChrome.GetWindowChrome(this).CaptionHeight = _focusPreview ? 0 : 64;
        WindowLayout.RowDefinitions[0].Height = new GridLength(_focusPreview ? 0 : 64);
        LibraryCard.Visibility = _focusPreview ? Visibility.Collapsed : _beforeFocusLibrary;
        LibrarySlot.Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        center.RowDefinitions[0].Height = new GridLength(_focusPreview ? 0 : 64);
        ((FrameworkElement)NavBar.Parent).Visibility = _focusPreview ? Visibility.Collapsed : Visibility.Visible;
        PlayerArea.SetFocusPreview(_focusPreview);
        WindowAppearance.SetFullscreenFrame(this, _focusPreview);
    }
    private void OnDialogOpened(object sender, DialogOpenedEventArgs e) => PlayerArea.SetDialogCovered(true);
    private void OnDialogClosed(object sender, DialogClosedEventArgs e) => PlayerArea.SetDialogCovered(false);
    private void OnInputWindowStateChanged(Window owner, bool covered)
    { if (ReferenceEquals(owner, this)) PlayerArea.SetDialogCovered(covered); }
    private async void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = AppConfig.LoadOrDefault(AppPaths.SettingsPath);
            var before = config.Appearance;
            var result = await AppearanceDialog.ShowAsync(before, async draft =>
            {
                await ApplyThemeAsync(draft.ThemeMode);
                await ApplyWallpaperAsync(draft);
            }, ApplyWallpaperOpacity);
            if (result is null) { await ApplyThemeAsync(before.ThemeMode); await ApplyWallpaperAsync(before); return; }
            await ApplyWallpaperAsync(result);
            config.Appearance = result; config.Save(AppPaths.SettingsPath);
            UiThemeService.Apply(result.ThemeMode);
        }
        catch (Exception ex) { await MessageDialog.ShowAsync(ex.Message, "外观设置", severity: DialogSeverity.Error); }
    }
    private int _wallpaperVersion;
    private async Task ApplyThemeAsync(UiThemeMode mode)
    {
        await ThemeTransition.Completion;
        var dark = mode == UiThemeMode.Dark || mode == UiThemeMode.System && MaterialDesignThemes.Wpf.Theme.GetSystemTheme() == MaterialDesignThemes.Wpf.BaseTheme.Dark;
        if (dark == UiThemeService.IsDark) { UiThemeService.Apply(mode); return; }
        await ThemeTransition.RunAsync(WindowSurface, MaterialDialogService.ThemeSurface, () => UiThemeService.Apply(mode));
    }
    private bool _wallpaperLoaded;
    private string? _wallpaperPath;
    internal async Task ApplyWallpaperAsync(AppearanceConfig appearance)
    {
        var version = ++_wallpaperVersion;
        var path = appearance.BackgroundImagePath;
        DefaultBackground.IsDark = UiThemeService.IsDark;
        if (string.IsNullOrWhiteSpace(path))
        {
            _wallpaperLoaded = true; _wallpaperPath = null;
            WallpaperImage.Source = null;
            WallpaperImage.Visibility = WallpaperShade.Visibility = Visibility.Collapsed;
            DefaultBackground.Visibility = Visibility.Visible;
            ApplyWallpaperOpacity(appearance.BackgroundOpacity);
            return;
        }
        if (_wallpaperLoaded && path == _wallpaperPath) { ApplyWallpaperOpacity(appearance.BackgroundOpacity); return; }
        var image = await Task.Run(() =>
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 2560;
            bitmap.UriSource = new Uri(System.IO.Path.GetFullPath(path));
            bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        });
        if (version != _wallpaperVersion) return;
        _wallpaperLoaded = true; _wallpaperPath = path; WallpaperImage.Source = image; ApplyWallpaperOpacity(appearance.BackgroundOpacity);
        DefaultBackground.Visibility = Visibility.Collapsed;
        WallpaperImage.Visibility = WallpaperShade.Visibility = Visibility.Visible;
    }
    internal void ApplyWallpaperOpacity(double opacity)
    {
        opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        WallpaperContent.Opacity = opacity;
    }
    private void StopMotion()
    {
        ++_noticeVersion; _noticeTimer.Stop();
        PrismMotion.StopAll(this, finish: true);
        PrismMotion.StopAll(NoticeTransform);
        PrismMotion.StopAll(LibraryScale, finish: true);
        PrismMotion.StopAll(LibraryTranslate, finish: true);
        PrismMotion.StopAll(LibraryCard, finish: true);
        if (SearchScope.Template.FindName("ScopeTranslate", SearchScope) is TranslateTransform slide)
            PrismMotion.StopAll(slide, finish: true);
    }
    internal void PrepareForVerificationExit()
    {
        // Explicit Application.Shutdown cannot await an asynchronous Closing handler.
        StopMotion();
        _closingFinished = true; PlayerArea.Detach();
    }
}
