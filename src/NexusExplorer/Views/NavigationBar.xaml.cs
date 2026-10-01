using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusExplorer.Models;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Views;

/// <summary>
/// 底部分类导航栏(双层):
/// 第一层 = 当前路径 + 逐层导航(点击下钻,右键可钉);
/// 第二层 = 快捷分类(钉住的分类,点击选中,右键取消);
/// 绿色打勾两层共用:把当前文件归入选中的分类。
/// </summary>
public partial class NavigationBar : UserControl
{
    private MainViewModel _main = null!;
    private NavigationViewModel Vm => _main.Navigation;

    // 快捷按钮选中高亮(与默认橙浅色区分)
    private static readonly Brush PinnedSelectedBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x8A, 0x3A));
    private static readonly Brush PinnedNormalBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xE2, 0xCE));

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
        Vm.Breadcrumb.CollectionChanged += (_, _) => UpdateActionBar();
        UpdateActionBar();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NavigationViewModel.Breadcrumb)
            or nameof(NavigationViewModel.HasCurrentFile)
            or nameof(NavigationViewModel.IsSelectedCategoryCurrent)
            or nameof(NavigationViewModel.SelectedCategory)
            or nameof(NavigationViewModel.PinnedCategories))
        {
            UpdateActionBar();
        }
    }

    private void UpdateActionBar()
    {
        RootBackButton.Visibility = Vm.Breadcrumb.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        ConfirmButton.IsEnabled = Vm.HasCurrentFile
            && Vm.SelectedCategory is not null
            && !Vm.IsSelectedCategoryCurrent;

        ConfirmButton.ToolTip = Vm.IsSelectedCategoryCurrent
            ? "文件已在此分类中"
            : Vm.SelectedCategory is null
                ? "先点击选择一个分类"
                : $"把文件归入「{Vm.SelectedCategory.Name}」";

        // 快捷层空提示
        PinnedEmptyHint.Visibility = Vm.PinnedCategories.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 快捷按钮选中高亮(选中分类 == 该按钮的分类)
        HighlightPinnedButtons(PinnedHost);
    }

    private void HighlightPinnedButtons(ItemsControl host)
    {
        foreach (var item in host.Items)
        {
            var container = host.ItemContainerGenerator.ContainerFromItem(item);
            if (container is null) continue;
            var button = FindVisualChild<Button>(container);
            if (button?.Tag is not Category category) continue;

            var selected = Vm.SelectedCategory?.Id == category.Id;
            button.Background = selected ? PinnedSelectedBrush : PinnedNormalBrush;
            button.Foreground = selected
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0xB4, 0x5F, 0x1D));
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
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

    /// <summary>点击快捷分类(第二层):选中它,面包屑同步到其所在链。</summary>
    private async void OnPinnedClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Category category }) return;
        try
        {
            await Vm.SelectPinnedAsync(category);
            UpdateActionBar();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "选择快捷分类失败");
            MessageBox.Show($"选择失败: {ex.Message}", "分类导航",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>右键取消钉住(入口在快捷分类按钮上)。</summary>
    private async void OnUnpinCategory(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuItem { Parent: ContextMenu menu } && menu.PlacementTarget is Button { Tag: Category category })
            {
                await _main.Category.UnpinAsync(category.Id);
                await Vm.OnPinsChangedAsync();
                UpdateActionBar();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "取消钉住失败");
            MessageBox.Show($"取消失败: {ex.Message}", "分类导航",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>绿色打勾:确认归类(两层共用)。</summary>
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
