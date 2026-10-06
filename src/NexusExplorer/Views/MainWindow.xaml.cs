using NexusExplorer.Views.Dialogs;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using NexusExplorer.Infrastructure;
using NexusExplorer.ApplicationLayer;
using MaterialDesignThemes.Wpf;

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
        TaskSnackbar.MessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(5));
        UiThemeService.Changed += OnThemeChanged;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        MaterialDialogService.NoticeRequested += OnNotice;
        Closed += (_, _) => { UiThemeService.Changed -= OnThemeChanged; MaterialDialogService.NoticeRequested -= OnNotice; TaskSnackbar.MessageQueue.Dispose(); };
        RootDialog.Identifier = "Nexus." + Guid.NewGuid().ToString("N");
        RootDialog.Loaded += (_, _) => MaterialDialogService.Register(RootDialog);
        RootDialog.Unloaded += (_, _) => MaterialDialogService.Unregister(RootDialog);
        PreviewKeyDown += async (_, e) =>
        {
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
    private void OnThemeChanged() => WindowAppearance.Apply(this);
    private void OnNotice(string message)
    {
        var preview = message.Length > 160 ? message[..160] + "…" : message;
        var text = new System.Windows.Controls.TextBlock { Text = preview, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        TaskSnackbar.MessageQueue?.Enqueue(text);
    }
    private void OnToggleDrawer(object sender, RoutedEventArgs e) => NavigationDrawer.IsLeftDrawerOpen = !NavigationDrawer.IsLeftDrawerOpen;
    private void OnDialogOpened(object sender, DialogOpenedEventArgs e) => PlayerArea.SetDialogCovered(true);
    private void OnDialogClosed(object sender, DialogClosedEventArgs e) => PlayerArea.SetDialogCovered(false);
    private async void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var choices = new[] { new DialogChoice<UiThemeMode>("跟随 Windows", UiThemeMode.System), new("浅色", UiThemeMode.Light), new("深色", UiThemeMode.Dark) };
            var result = await ChoiceDialog.ShowAsync("外观设置", "界面主题", choices, UiThemeService.Mode);
            if (result.Confirmed) UiThemeService.Apply(result.Value, save: true);
        }
        catch (Exception ex) { await MessageDialog.ShowAsync(ex.Message, "外观设置", severity: DialogSeverity.Error); }
    }
    internal void PrepareForVerificationExit()
    {
        // Explicit Application.Shutdown cannot await an asynchronous Closing handler.
        _closingFinished = true; PlayerArea.Detach();
    }
}
