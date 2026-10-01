using System.Windows;
using System.Windows.Controls;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Views;

/// <summary>
/// 底部分类导航栏:点击分类=导航下钻(不改归属),
/// 绿色打勾=把当前文件归入选中的分类;已属于该分类时置灰。
/// </summary>
public partial class NavigationBar : UserControl
{
    private MainViewModel _main = null!;
    private NavigationViewModel Vm => _main.Navigation;

    public NavigationBar()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel main)
    {
        _main = main;
        Vm.ShowError = message =>
            MessageBox.Show(message, "分类导航", MessageBoxButton.OK, MessageBoxImage.Warning);

        DataContext = Vm;
        Vm.PropertyChanged += OnVmPropertyChanged;
        UpdateActionBar();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NavigationViewModel.Breadcrumb)
            or nameof(NavigationViewModel.HasCurrentFile)
            or nameof(NavigationViewModel.IsSelectedCategoryCurrent)
            or nameof(NavigationViewModel.SelectedCategory))
        {
            UpdateActionBar();
        }
    }

    private void UpdateActionBar()
    {
        RootBackButton.Visibility = Vm.Breadcrumb.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 打勾按钮:无文件/未选分类/已属于选中分类 → 禁用
        ConfirmButton.IsEnabled = Vm.HasCurrentFile
            && Vm.SelectedCategory is not null
            && !Vm.IsSelectedCategoryCurrent;

        // 选中分类的提示
        ConfirmButton.ToolTip = Vm.IsSelectedCategoryCurrent
            ? "文件已在此分类中"
            : Vm.SelectedCategory is null
                ? "先点击选择一个分类"
                : $"把文件归入「{Vm.SelectedCategory.Name}」";
    }

    /// <summary>点击子分类:导航下钻并选中。</summary>
    private async void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Category category }) return;
        try { await Vm.NavigateToAsync(category); }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "导航栏下钻失败");
            MessageBox.Show($"导航失败: {ex.Message}", "分类导航",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>面包屑回退。</summary>
    private async void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Category category }) return;
        try
        {
            var index = Vm.Breadcrumb.IndexOf(category);
            if (index >= 0)
                await Vm.NavigateBackToAsync(index);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "导航栏回退失败");
        }
    }

    private async void OnBackToRoot(object sender, RoutedEventArgs e)
    {
        try { await Vm.NavigateBackToAsync(-1); }
        catch (Exception ex) { Serilog.Log.Error(ex, "导航栏回根失败"); }
    }

    /// <summary>绿色打勾:确认归类。</summary>
    private async void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        try { await Vm.ConfirmRecategorizeAsync(); }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "确认归类失败");
            MessageBox.Show($"归类失败: {ex.Message}", "分类导航",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
