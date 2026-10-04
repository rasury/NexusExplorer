using MessageBox = NexusExplorer.Views.Dialogs.MessageDialog;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _main;

    public MainWindow(MainViewModel main, CategoryService categoryService,
        FileService fileService, OrganizationService organizationService,
        IRecycleBinService recycleBin, MediaPlayerService mediaPlayer)
    {
        InitializeComponent();

        _main = main;

        // 注入面板依赖
        LeftPanel.Initialize(_main, recycleBin);
        PlayerArea.Initialize(_main);
        NavBar.Initialize(_main);

        Loaded += async (_, _) =>
        {
            try { await _main.RefreshTreeAsync(); await LeftPanel.RestoreBrowseAsync(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "加载界面失败"); MessageBox.Show(ex.Message, "加载失败"); }
        };
        Closing += async (_, e) =>
        {
            if (_closingFinished) return;
            e.Cancel = true;
            if (_closingStarted) return;
            _closingStarted = true; IsEnabled = false;
            try { await LeftPanel.PrepareForCloseAsync(); LeftPanel.SaveUiState(); await mediaPlayer.StopAndReleaseAsync(); PlayerArea.Detach(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "退出时停止播放失败"); }
            _closingFinished = true; Close();
        };
    }
    private bool _closingStarted;
    private bool _closingFinished;
    internal void PrepareForVerificationExit()
    {
        // Explicit Application.Shutdown cannot await an asynchronous Closing handler.
        _closingFinished = true; PlayerArea.Detach();
    }
}
