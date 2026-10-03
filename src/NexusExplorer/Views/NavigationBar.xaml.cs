using System.Windows;
using System.Windows.Controls;
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
    private const string PinnedFormat = "NexusExplorer.PinnedCategory";
    private Point _pinStart;
    private Category? _pressedPin;
    private bool _reordering;
    private void OnPinnedMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressedPin = CategoryFilePanel.FindAncestor<Button>(e.OriginalSource as DependencyObject)?.Tag as Category;
        _pinStart = e.GetPosition(PinnedHost);
    }
    private void OnPinnedMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pressedPin is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var p = e.GetPosition(PinnedHost);
        if (Math.Abs(p.X - _pinStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _pinStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var id = _pressedPin.Id; _pressedPin = null; _reordering = true;
        try { DragDrop.DoDragDrop(PinnedHost, new DataObject(PinnedFormat, id), DragDropEffects.Move); }
        finally { _reordering = false; }
    }
    private void OnPinnedDragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(PinnedFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }
    private async void OnPinnedDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(PinnedFormat) is not int id) return;
        try
        {
            var button = CategoryFilePanel.FindAncestor<Button>(e.OriginalSource as DependencyObject);
            var remaining = Vm.PinnedCategories.Where(c => c.Id != id).ToList();
            var index = remaining.Count;
            if (button?.Tag is Category target)
            {
                if (target.Id == id) return;
                index = remaining.FindIndex(c => c.Id == target.Id);
                if (e.GetPosition(button).X > button.ActualWidth / 2) index++;
            }
            await _main.Categories.ReorderPinnedAsync(id, index); await Vm.OnPinsChangedAsync();
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "快捷分类排序失败"); MessageBox.Show(ex.Message, "排序失败"); }
    }
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
        Unloaded += (_, _) => Vm.PropertyChanged -= OnVmPropertyChanged;
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
        if (_reordering) return;
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
