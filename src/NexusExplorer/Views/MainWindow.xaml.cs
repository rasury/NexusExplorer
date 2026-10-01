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

        Loaded += async (_, _) => await _main.RefreshTreeAsync();
        Closing += (_, _) =>
        {
            mediaPlayer.Stop();
            mediaPlayer.Dispose();
        };
    }
}
