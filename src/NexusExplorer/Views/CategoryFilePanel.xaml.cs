using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;

namespace NexusExplorer.Views;

public partial class CategoryFilePanel : UserControl
{
    private MainViewModel _main = null!;
    private CategoryViewModel CategoryVm => _main.Category;
    private FileListViewModel FileListVm => _main.FileList;

    // XAML 绑定入口
    public CategoryViewModel? CategoryVmBinding => _main?.Category;
    public FileListViewModel? FileListVmBinding => _main?.FileList;

    // 拖拽进行中的分类(内部拖拽)
    private Category? _dragCategory;
    private FileItem? _dragFile;

    // 拖拽阈值:按下起点,移动超过系统阈值才发起拖拽,
    // 否则双击/单击的微小移动会被劫持成拖拽(DoDragDrop 模态循环吞掉后续点击)
    private Point _dragStartPoint;
    private bool _isPotentialDrag;

    public CategoryFilePanel()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void Initialize(MainViewModel main, IRecycleBinService recycleBin)
    {
        _main = main;

        // 触发绑定刷新(CategoryVmBinding / FileListVmBinding 从 null 变为有值)
        DataContext = null;
        DataContext = this;

        CategoryVm.RecycleBin = recycleBin;
        FileListVm.RecycleBin = recycleBin;

        // 对话框注入
        CategoryVm.ShowInputDialog = (title, defaultValue) =>
            Dialogs.InputDialog.Show(title, "分类名称:", defaultValue) is { } v && v.Trim().Length > 0
                ? Task.FromResult<string?>(v.Trim())
                : Task.FromResult<string?>(null);

        CategoryVm.ShowConfirmDialog = message =>
            Task.FromResult(MessageBox.Show(message, "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

        CategoryVm.ShowError = message =>
            MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);

        FileListVm.ShowError = message =>
            MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);

        FileListVm.ShowInfo = message =>
            MessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);

        FileListVm.ShowConfirmDialog = message =>
            Task.FromResult(MessageBox.Show(message, "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

        FileListVm.PickFiles = () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择文件",
                Multiselect = true
            };
            return Task.FromResult(dialog.ShowDialog() == true
                ? dialog.FileNames.ToList() as IReadOnlyList<string>
                : Array.Empty<string>());
        };

        FileListVm.PickDirectory = () =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择文件夹"
            };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
        };

        FileListVm.PickRelocateFile = fileName =>
        {
            var dialog = new OpenFileDialog
            {
                Title = $"重新定位「{fileName}」",
                Filter = "所有文件|*.*"
            };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
        };

        _main.CategoryTreeChanged += RefreshTree;
        _main.FileListChanged += RefreshFileList;
        _main.CurrentFileChanged += OnCurrentFileChanged;

        RefreshTree();
    }

    /// <summary>当前播放文件变化(播放器上一项/下一项等)→ 左侧列表同步选中。</summary>
    private void OnCurrentFileChanged(Models.FileItem? file)
    {
        if (file is null) return;
        if (ReferenceEquals(FileListBox.SelectedItem, file)) return;
        FileListBox.SelectedItem = file;
        FileListBox.ScrollIntoView(file);
    }

    // ---------- 刷新 ----------

    private void RefreshTree()
    {
        var selectedId = _main.CurrentCategory?.Id;
        CategoryTree.ItemsSource = CategoryVm.RootCategories;

        if (selectedId is not null)
            ExpandAndSelect(CategoryVm.RootCategories, selectedId.Value);

        UpdateOrganizeEnabled();
    }

    private void RefreshFileList()
    {
        FileListBox.ItemsSource = FileListVm.Files;
        FileListVm.UpdateHeader();
        // 刷新后恢复当前播放文件的选中
        if (_main.CurrentFile is not null && FileListVm.Files.Contains(_main.CurrentFile))
            FileListBox.SelectedItem = _main.CurrentFile;
        UpdateOrganizeEnabled();
    }

    private void UpdateOrganizeEnabled()
    {
    }

    private bool ExpandAndSelect(IEnumerable<Category> categories, int id)
    {
        foreach (var category in categories)
        {
            if (category.Id == id)
            {
                CategoryTree.SelectedItemChanged -= OnCategorySelected;
                // WPF TreeView 不支持直接设置 SelectedItem;用容器方式
                SelectContainer(CategoryTree, category);
                CategoryTree.SelectedItemChanged += OnCategorySelected;
                return true;
            }
            if (category.Children.Count > 0 && ExpandAndSelect(category.Children, id))
            {
                Expand(category);
                return true;
            }
        }
        return false;
    }

    private static void Expand(Category category)
    {
        // 通过 ItemsControl 容器展开
        // 简化:ViewModel 需要时树本身记住展开状态成本高,V1 直接展开祖先路径
    }

    private void SelectContainer(ItemsControl parent, Category target)
    {
        if (parent.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            Dispatcher.BeginInvoke(() => SelectContainer(parent, target));
            return;
        }

        foreach (var item in parent.Items)
        {
            if (item is not Category category) continue;

            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (category.Id == target.Id)
            {
                if (container is not null)
                    container.IsSelected = true;
                return;
            }
            if (category.Children.Count > 0 && container is not null)
            {
                container.IsExpanded = true;
                SelectContainer(container, target);
            }
        }
    }

    // ---------- 分类树事件 ----------

    private async void OnCategorySelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not Category category) return;
        try
        {
            await _main.SelectCategoryAsync(category);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "选择分类时出错: {Category}", category.Name);
            ShowErrorSafe($"切换分类失败: {ex.Message}");
        }
    }

    private void ShowErrorSafe(string message) =>
        MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);

    private async void OnCreateChild(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        try { await CategoryVm.CreateChildAsync(category); }
        catch (Exception ex) { Serilog.Log.Error(ex, "新建子分类失败"); ShowErrorSafe(ex.Message); }
    }

    private async void OnRenameCategory(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        try { await CategoryVm.RenameAsync(category); }
        catch (Exception ex) { Serilog.Log.Error(ex, "重命名分类失败"); ShowErrorSafe(ex.Message); }
    }

    private async void OnDeleteCategory(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        try { await CategoryVm.DeleteAsync(category); }
        catch (Exception ex) { Serilog.Log.Error(ex, "删除分类失败"); ShowErrorSafe(ex.Message); }
    }

    private async void OnMoveCategoryUp(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        try { await CategoryVm.MoveUpAsync(category); }
        catch (Exception ex) { Serilog.Log.Error(ex, "分类上移失败"); ShowErrorSafe(ex.Message); }
    }

    private async void OnMoveCategoryDown(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        try { await CategoryVm.MoveDownAsync(category); }
        catch (Exception ex) { Serilog.Log.Error(ex, "分类下移失败"); ShowErrorSafe(ex.Message); }
    }

    // ---------- 文件列表事件 ----------

    private async void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListBox.SelectedItem is not FileItem file) return;
        try { await FileListVm.ActivateFileAsync(file); }
        catch (Exception ex) { Serilog.Log.Error(ex, "打开文件失败"); ShowErrorSafe(ex.Message); }
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        // 单击仅预览标题,不自动播放;双击播放
    }

    /// <summary>钉到底栏快捷分类层。</summary>
    private async void OnPinCategory(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        try
        {
            var categoryService = AppServices.Categories;
            if (categoryService is null) return;
            await categoryService.PinAsync(category.Id);
            // 刷新底栏快捷层
            await _main.Navigation.OnPinsChangedAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "钉住分类失败");
            ShowErrorSafe($"钉住失败: {ex.Message}");
        }
    }

    /// <summary>在资源管理器中打开分类对应的物理目录。</summary>
    private void OnOpenCategoryInExplorer(object sender, RoutedEventArgs e)
    {
        if (CategoryTree.SelectedItem is not Category category) return;
        if (!Services.ExplorerService.OpenDirectory(category.PhysicalPath))
            MessageBox.Show($"目录不存在:\n{category.PhysicalPath}", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>在资源管理器中定位并选中文件。</summary>
    private void OnOpenFileInExplorer(object sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is not FileItem file) return;
        if (!Services.ExplorerService.RevealFile(file.AbsolutePath))
            MessageBox.Show($"文件不存在(可能已被外部移动或删除):\n{file.AbsolutePath}\n\n可右键选择「重新定位」。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnRemoveFile(object sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is not FileItem file) return;
        try { await FileListVm.RemoveFileAsync(file); }
        catch (Exception ex) { Serilog.Log.Error(ex, "移除文件失败"); ShowErrorSafe(ex.Message); }
    }

    private async void OnDeleteFile(object sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is not FileItem file) return;
        try { await FileListVm.DeleteFileAsync(file); }
        catch (Exception ex) { Serilog.Log.Error(ex, "删除文件失败"); ShowErrorSafe(ex.Message); }
    }

    private async void OnRelocateFile(object sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is not FileItem file) return;
        try { await FileListVm.RelocateFileAsync(file); }
        catch (Exception ex) { Serilog.Log.Error(ex, "重新定位失败"); ShowErrorSafe(ex.Message); }
    }

    // ---------- 整理 ----------

    private bool CanOrganize() => _main.CurrentCategory is not null;

    private async Task OrganizeAsync()
    {
        if (_main.CurrentCategory is null) return;

        var organizationService = AppServices.Organization;
        if (organizationService is null) return;

        // 正在播放的文件被 VLC 占用,File.Move 会报
        // "being used by another process" — 整理前先停止播放;
        // VLC 停止是异步的,稍等句柄释放
        if (_main.CurrentFile is not null)
        {
            await _main.StopPlaybackForOrganizeAsync();
            await Task.Delay(300);
        }

        var result = await organizationService.OrganizeAsync(
            _main.CurrentCategory.Id,
            (fileName, targetPath) =>
                Task.FromResult(Dispatcher.Invoke(() => Dialogs.ConflictDialog.Show(fileName, targetPath))));

        await _main.RefreshFilesAsync();
        Dialogs.OrganizeResultDialog.Show(_main.CurrentCategory.Name, result);
    }

    private System.Windows.Input.ICommand? _organizeCommand;

    public System.Windows.Input.ICommand OrganizeCommand =>
        _organizeCommand ??= new RelayCommand(OrganizeAsync, CanOrganize);

    // ---------- 拖拽:Explorer → 软件 ----------

    private void OnFilesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _main.CurrentCategory is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFilesDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        try { await FileListVm.ImportDroppedPathsAsync(paths); }
        catch (Exception ex) { Serilog.Log.Error(ex, "拖入文件失败"); ShowErrorSafe(ex.Message); }
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else if (_dragCategory is not null || _dragFile is not null)
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void OnTreeDrop(object sender, DragEventArgs e)
    {
        try
        {
        await OnTreeDropCore(e);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "分类树拖放处理失败");
            ShowErrorSafe($"拖放操作失败: {ex.Message}");
        }
        finally
        {
            _dragCategory = null;
            _dragFile = null;
        }
    }

    private async Task OnTreeDropCore(DragEventArgs e)
    {
        // Explorer 拖入文件/文件夹 → 文件夹镜像导入(目录结构→同名分类),散文件直接加入
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            var target = GetCategoryFromDrop(e);
            if (target is null) return;

            var categoryService = AppServices.Categories;
            if (categoryService is null) return;

            var looseFiles = new List<string>();
            var added = 0;
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var result = await AppServices.FileService!.ImportDirectoryAsync(path, target.Id, categoryService);
                    added += result.Added.Count;
                }
                else if (File.Exists(path))
                {
                    looseFiles.Add(path);
                }
            }
            if (looseFiles.Count > 0)
            {
                var result = await AppServices.FileService!.AddRangeAsync(looseFiles, target.Id);
                added += result.Added.Count;
            }
            if (added == 0) return;
            await _main.RefreshTreeAsync();
            await _main.RefreshFilesAsync();
            MessageBox.Show($"已导入 {added} 个文件到「{target.Name}」(文件夹已按目录结构创建对应分类)", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 内部拖拽:分类 → 分类
        var dropTarget = GetCategoryFromDrop(e);
        if (_dragCategory is not null && dropTarget is not null)
        {
            if (dropTarget.Id != _dragCategory.Id)
                await CategoryVm.MoveAsync(_dragCategory, dropTarget);
            return;
        }

        // 内部拖拽:文件 → 分类(重新分类)
        if (_dragFile is not null && dropTarget is not null)
        {
            await FileListVm.RecategorizeAsync(_dragFile, dropTarget);
        }
    }

    private Category? GetCategoryFromDrop(DragEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is TreeViewItem item && item.Header is Category category)
                return category;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return CategoryTree.SelectedItem as Category;
    }

    // ---------- 拖拽发起(内部) ----------

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        _dragFile = FileListBox.SelectedItem as FileItem;
        _dragStartPoint = e.GetPosition(null);
        _isPotentialDrag = e.ChangedButton == MouseButton.Left;
    }

    private bool ExceedsDragThreshold(MouseEventArgs e)
    {
        if (!_isPotentialDrag) return false;
        var position = e.GetPosition(null);
        return Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (e.LeftButton == MouseButtonState.Pressed && ExceedsDragThreshold(e))
        {
            _isPotentialDrag = false; // 阈值只判定一次
            // 拖拽文件
            if (_dragFile is not null && IsOverElement(FileListBox, e))
            {
                DragDrop.DoDragDrop(FileListBox, new DataObject(DataFormats.StringFormat, _dragFile.FileName), DragDropEffects.Move);
                return;
            }
            // 拖拽分类
            if (CategoryTree.SelectedItem is Category category && IsOverElement(CategoryTree, e))
            {
                _dragCategory = category;
                DragDrop.DoDragDrop(CategoryTree, new DataObject(DataFormats.StringFormat, category.Name), DragDropEffects.Move);
                _dragCategory = null;
            }
        }
    }

    private bool IsOverElement(UIElement element, MouseEventArgs e)
    {
        var position = e.GetPosition(element);
        return position.X >= 0 && position.Y >= 0
               && position.X <= element.RenderSize.Width && position.Y <= element.RenderSize.Height;
    }
}

// RelayCommand 简版(避免引入额外依赖)
file class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute();
    public async void Execute(object? parameter) => await _executeAsync();
}
