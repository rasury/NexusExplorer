using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusExplorer.Infrastructure;
using NexusExplorer.Models;
using NexusExplorer.Services;
using NexusExplorer.ViewModels;
using Serilog;

namespace NexusExplorer.Views;

public partial class CategoryFilePanel : UserControl
{
    private const string FilesFormat = "NexusExplorer.FileIds";
    private const string CategoryFormat = "NexusExplorer.CategoryId";
    private MainViewModel _main = null!;
    private CategoryViewModel CategoryVm => _main.Category;
    private FileListViewModel FileListVm => _main.FileList;
    public CategoryViewModel? CategoryVmBinding => _main?.Category;
    public FileListViewModel? FileListVmBinding => _main?.FileList;
    private Category? _contextMenuCategory;
    private Point _start;
    private FileItem? _pressedFile;
    private Category? _pressedCategory;
    private bool _potentialDrag;
    private bool _deferSelection;
    private bool _restored;
    private bool _shuttingDown;
    private UiStateStore _state = new();
    private CancellationTokenSource? _organizing;
    private AsyncRelayCommand? _organizeCommand;
    public ICommand OrganizeCommand => _organizeCommand ??= new AsyncRelayCommand(OrganizeAsync, () => _main?.CurrentCategory is not null && _organizing is null);

    public CategoryFilePanel() { InitializeComponent(); DataContext = this; }
    public void Initialize(MainViewModel main, IRecycleBinService recycleBin)
    {
        _main = main; DataContext = null; DataContext = this; _state = UiStateStore.Load();
        CategoryVm.RecycleBin = recycleBin; FileListVm.RecycleBin = recycleBin;
        CategoryVm.ShowInputDialog = (title, value) => Task.FromResult(Dialogs.InputDialog.Show(title, "分类名称:", value));
        CategoryVm.ShowConfirmDialog = ConfirmAsync; FileListVm.ShowConfirmDialog = ConfirmAsync;
        CategoryVm.ShowError = ShowError; FileListVm.ShowError = ShowError;
        FileListVm.ShowInfo = message => OperationStatus.Text = message;
        FileListVm.ShowImportStatus = message => OperationStatus.Text = message;
        FileListVm.PickFiles = () =>
        {
            var dialog = new OpenFileDialog { Title = "选择文件", Multiselect = true };
            return Task.FromResult<IReadOnlyList<string>>(dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>());
        };
        FileListVm.PickDirectory = () => Task.FromResult(PickDirectory("选择导入文件夹"));
        FileListVm.PickRelocateFile = name =>
        {
            var dialog = new OpenFileDialog { Title = $"重新定位「{name}」" };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
        };
        main.CategoryTreeChanged += RefreshTree; main.FileListChanged += RefreshFileList;
        main.CurrentFileChanged += OnCurrentFileChanged;
        Unloaded += OnUnloaded;
    }
    private static Task<bool> ConfirmAsync(string message) => Task.FromResult(MessageBox.Show(message, "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    private static void ShowError(string message) => MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
    private static string? PickDirectory(string title)
    { var dialog = new OpenFolderDialog { Title = title }; return dialog.ShowDialog() == true ? dialog.FolderName : null; }
    private static async Task RunAsync(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Log.Error(ex, "界面操作失败"); ShowError(ex.Message); } }
    internal static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T found) return found;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
    private static T? FindChild<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); if (child is T found) return found; if (FindChild<T>(child) is { } nested) return nested; }
        return null;
    }
    internal static bool IsControlChrome(DependencyObject? source) => FindAncestor<ScrollBar>(source) is not null || FindAncestor<Thumb>(source) is not null || FindAncestor<ButtonBase>(source) is not null;
    internal static FileItem? HitFile(DependencyObject? source) => IsControlChrome(source) ? null : FindAncestor<ListBoxItem>(source)?.DataContext as FileItem;
    internal static Category? HitCategory(DependencyObject? source) => IsControlChrome(source) ? null : FindAncestor<TreeViewItem>(source)?.Header as Category;
    private void RefreshTree()
    {
        var offset = !_restored ? _state.TreeOffset : FindChild<ScrollViewer>(CategoryTree)?.VerticalOffset ?? 0;
        if (!_restored)
        {
            foreach (var c in CategoryVm.FlatCategories) { c.IsExpanded = _state.Expanded.Contains(c.Id); c.IsSelected = c.Id == _state.SelectedCategoryId; }
            _restored = true;
        }
        CategoryTree.ItemsSource = CategoryVm.RootCategories;
        _ = Dispatcher.BeginInvoke(() => FindChild<ScrollViewer>(CategoryTree)?.ScrollToVerticalOffset(offset));
        _organizeCommand?.NotifyCanExecuteChanged();
    }
    private void RefreshFileList()
    {
        var ids = FileListBox.SelectedItems.Cast<FileItem>().Select(f => f.Id).ToHashSet();
        var offset = FindChild<ScrollViewer>(FileListBox)?.VerticalOffset ?? 0;
        FileListBox.ItemsSource = FileListVm.Files; FileListVm.UpdateHeader();
        foreach (var f in FileListVm.Files.Where(f => ids.Contains(f.Id))) FileListBox.SelectedItems.Add(f);
        _ = Dispatcher.BeginInvoke(() => FindChild<ScrollViewer>(FileListBox)?.ScrollToVerticalOffset(offset));
        _organizeCommand?.NotifyCanExecuteChanged();
    }
    public async Task RestoreBrowseAsync()
    {
        if (_state.BrowsedCategoryId is int id && await _main.Categories.GetByIdAsync(id) is { } category)
        { await _main.SelectCategoryAsync(category); FindChild<ScrollViewer>(FileListBox)?.ScrollToVerticalOffset(_state.FileOffset); }
    }
    public void SaveUiState()
    {
        if (_main is null) return;
        _state.Expanded = CategoryVm.FlatCategories.Where(c => c.IsExpanded).Select(c => c.Id).ToHashSet();
        _state.SelectedCategoryId = (CategoryTree.SelectedItem as Category)?.Id;
        _state.BrowsedCategoryId = _main.CurrentCategory?.Id;
        _state.TreeOffset = FindChild<ScrollViewer>(CategoryTree)?.VerticalOffset ?? 0;
        _state.FileOffset = FindChild<ScrollViewer>(FileListBox)?.VerticalOffset ?? 0;
        _state.Save();
    }
    public async Task PrepareForCloseAsync()
    {
        _shuttingDown = true; _organizing?.Cancel();
        if (_organizeCommand?.ExecutionTask is { } task) await task;
        using var lease = await MutationGate.AcquireAsync();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SaveUiState();
        _main.CategoryTreeChanged -= RefreshTree; _main.FileListChanged -= RefreshFileList; _main.CurrentFileChanged -= OnCurrentFileChanged;
        Unloaded -= OnUnloaded;
    }
    private void OnCurrentFileChanged(FileItem? file)
    {
        if (file is not null && FileListVm.Files.FirstOrDefault(f => f.Id == file.Id) is { } visible) FileListBox.ScrollIntoView(visible);
    }
    private async void OnCategoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsControlChrome(e.OriginalSource as DependencyObject)) return;
        if (FindAncestor<TextBlock>(e.OriginalSource as DependencyObject) is null) return;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.Header is not Category c) return;
        e.Handled = true; await RunAsync(() => _main.SelectCategoryAsync(c));
    }
    private async void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsControlChrome(e.OriginalSource as DependencyObject)) return;
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not FileItem file) return;
        e.Handled = true; await RunAsync(() => FileListVm.ActivateFileAsync(file));
    }
    private void OnCategoryTreeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextMenuCategory = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.Header as Category;
        if (_contextMenuCategory is null) e.Handled = true;
    }
    internal Category? GetContextMenuCategory() => _contextMenuCategory;
    internal void RecordContextMenuTarget(TreeViewItem item) => _contextMenuCategory = item.Header as Category;
    internal void RecordContextMenuSource(DependencyObject source) => _contextMenuCategory = FindAncestor<TreeViewItem>(source)?.Header as Category;
    private void OnFileContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not FileItem f) { e.Handled = true; return; }
        if (!FileListBox.SelectedItems.Contains(f)) { FileListBox.SelectedItems.Clear(); FileListBox.SelectedItems.Add(f); }
        if (FileListBox.ContextMenu?.Items[1] is MenuItem relocate) relocate.IsEnabled = FileListBox.SelectedItems.Count == 1;
    }
    private async Task CategoryActionAsync(Func<Category, Task> action)
    { if (_contextMenuCategory is { } c) await RunAsync(() => action(c)); }
    private async void OnCreateChild(object sender, RoutedEventArgs e) => await CategoryActionAsync(CategoryVm.CreateChildAsync);
    private async void OnRenameCategory(object sender, RoutedEventArgs e) => await CategoryActionAsync(CategoryVm.RenameAsync);
    private async void OnDeleteCategory(object sender, RoutedEventArgs e) => await CategoryActionAsync(CategoryVm.DeleteAsync);
    private async void OnMoveCategoryUp(object sender, RoutedEventArgs e) => await CategoryActionAsync(CategoryVm.MoveUpAsync);
    private async void OnMoveCategoryDown(object sender, RoutedEventArgs e) => await CategoryActionAsync(CategoryVm.MoveDownAsync);
    private async void OnMoveCategoryToRoot(object sender, RoutedEventArgs e) => await CategoryActionAsync(c => CategoryVm.MoveAsync(c, null));
    private async void OnPinCategory(object sender, RoutedEventArgs e) => await CategoryActionAsync(async c =>
    { await _main.Categories.PinAsync(c.Id); await _main.Navigation.OnPinsChangedAsync(); });
    private async void OnRelocateCategory(object sender, RoutedEventArgs e) => await CategoryActionAsync(async c =>
    {
        var path = PickDirectory("选择分类的新位置（不搬文件）"); if (path is null) return;
        var preview = await _main.Categories.PreviewRelocateAsync(c.Id, path);
        if (!Dialogs.LocationPreviewDialog.Confirm(preview)) return;
        await _main.Categories.RelocateAsync(c.Id, path); await _main.RefreshTreeAsync();
    });
    private async void OnMigrateCategory(object sender, RoutedEventArgs e) => await CategoryActionAsync(async c =>
    {
        var parent = PickDirectory("选择迁移目标父目录"); if (parent is null) return;
        if (!await ConfirmAsync($"迁移完整物理目录：\n{c.PhysicalPath}\n→ {Path.Combine(parent, Path.GetFileName(c.PhysicalPath))}\n包含未登记文件。继续？")) return;
        await _main.Categories.MigrateDirectoryAsync(c.Id, parent); await _main.RefreshTreeAsync();
    });
    private void OnOpenCategoryInExplorer(object sender, RoutedEventArgs e)
    { if (_contextMenuCategory is { } c && !ExplorerService.OpenDirectory(c.PhysicalPath)) ShowError("目录不存在，请重新定位。"); }
    private void OnOpenFileInExplorer(object sender, RoutedEventArgs e)
    { if (FileListBox.SelectedItem is FileItem f && !ExplorerService.RevealFile(f.AbsolutePath)) ShowError("文件不存在，请重新定位。"); }
    private IReadOnlyList<FileItem> SelectedFiles() => FileListBox.SelectedItems.Cast<FileItem>().ToList();
    private async void OnRemoveFile(object sender, RoutedEventArgs e) => await RunAsync(() => FileListVm.DeleteManyAsync(SelectedFiles(), true));
    private async void OnDeleteFile(object sender, RoutedEventArgs e) => await RunAsync(() => FileListVm.DeleteManyAsync(SelectedFiles(), false));
    private async void OnRelocateFile(object sender, RoutedEventArgs e)
    { if (FileListBox.SelectedItems.Count == 1 && FileListBox.SelectedItem is FileItem f) await RunAsync(() => FileListVm.RelocateFileAsync(f)); }

    private async Task OrganizeAsync()
    {
        var category = _main.CurrentCategory; if (category is null || _organizing is not null) return;
        _organizing = new(); CancelOrganizeButton.Visibility = Visibility.Visible; _organizeCommand?.NotifyCanExecuteChanged();
        try
        {
            Dialogs.ConflictDecision? policy = null; var cancelledByDialog = false;
            var progress = new Progress<OrganizeProgress>(p => OperationStatus.Text = $"整理 {p.Completed}/{p.Total}: {p.FileName}");
            var result = await _main.Organization.OrganizeAsync(category.Id, (name, target) =>
            {
                var decision = policy ?? Dialogs.ConflictDialog.ShowDecision(name, target);
                if (decision.Resolution == ConflictResolution.Ask) cancelledByDialog = true;
                if (decision.ApplyToAll && decision.Resolution != ConflictResolution.Ask) policy = decision;
                return Task.FromResult(decision.Resolution);
            }, _organizing.Token, progress);
            await _main.RefreshFilesAsync();
            OperationStatus.Text = _organizing.IsCancellationRequested || cancelledByDialog ? $"整理已取消，已处理 {result.Count} 项" : $"整理完成，已处理 {result.Count} 项";
            if (!_shuttingDown) Dialogs.OrganizeResultDialog.Show(category.Name, result);
        }
        catch (Exception ex) { Log.Error(ex, "整理失败"); ShowError(ex.Message); OperationStatus.Text = "整理失败，请查看日志"; }
        finally { _organizing.Dispose(); _organizing = null; CancelOrganizeButton.Visibility = Visibility.Collapsed; _organizeCommand?.NotifyCanExecuteChanged(); }
    }
    private void OnCancelOrganize(object sender, RoutedEventArgs e) => _organizing?.Cancel();
    private void OnFilesDragOver(object sender, DragEventArgs e)
    { e.Effects = _main.CurrentCategory is not null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void OnFilesDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (_main.CurrentCategory is { } target) QueueExternalImport(paths, target);
        else OperationStatus.Text = "请先双击打开分类，再拖入文件。";
    }
    private void QueueExternalImport(string[] paths, Category target)
    {
        // Copy OLE data before returning Drop; never keep Explorer's drag loop in a dialog.
        var copiedPaths = paths.ToArray();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, (Action)(async () =>
        {
            OperationStatus.Text = $"正在导入到「{target.Name}」…";
            try { await FileListVm.ImportIntoAsync(copiedPaths, target); }
            catch (Exception ex) { Log.Error(ex, "外部拖入失败"); OperationStatus.Text = $"导入失败：{ex.Message}"; }
        }));
    }
    private Category? DropTarget(DragEventArgs e) => IsControlChrome(e.OriginalSource as DependencyObject) ? null : FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.Header as Category;
    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DropTarget(e) is null ? DragDropEffects.None :
            e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy :
            e.Data.GetDataPresent(FilesFormat) || e.Data.GetDataPresent(CategoryFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnTreeDrop(object sender, DragEventArgs e)
    {
        e.Handled = true; var target = DropTarget(e); if (target is null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] externalPaths) { QueueExternalImport(externalPaths, target); return; }
        await RunAsync(async () =>
        {
            if (e.Data.GetData(CategoryFormat) is int id && await _main.Categories.GetByIdAsync(id) is { } c) await CategoryVm.MoveAsync(c, target);
            else if (e.Data.GetData(FilesFormat) is int[] ids)
            {
                var files = new List<FileItem>(); foreach (var fileId in ids) if (await _main.Files.GetByIdAsync(fileId) is { } f) files.Add(f);
                await FileListVm.RecategorizeManyAsync(files, target);
            }
        });
    }
    private void OnRootDragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(CategoryFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }
    private async void OnRootDrop(object sender, DragEventArgs e)
    { e.Handled = true; if (e.Data.GetData(CategoryFormat) is int id) await RunAsync(async () => { if (await _main.Categories.GetByIdAsync(id) is { } c) await CategoryVm.MoveAsync(c, null); }); }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e); _potentialDrag = false; _pressedFile = null; _pressedCategory = null; _deferSelection = false;
        var source = e.OriginalSource as DependencyObject;
        if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1 || IsControlChrome(source)) return;
        _pressedFile = HitFile(source);
        _pressedCategory = HitCategory(source);
        if (_pressedFile is null && _pressedCategory is null) return;
        _start = e.GetPosition(this); _potentialDrag = true;
        if (_pressedFile is not null && Keyboard.Modifiers == ModifierKeys.None && FileListBox.SelectedItems.Contains(_pressedFile))
        { _deferSelection = true; e.Handled = true; }
    }
    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (_deferSelection && _pressedFile is not null && _potentialDrag)
        { FileListBox.SelectedItems.Clear(); FileListBox.SelectedItems.Add(_pressedFile); }
        _potentialDrag = false; _deferSelection = false;
    }
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e); if (!_potentialDrag || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _potentialDrag = false;
        try
        {
            if (_pressedFile is not null)
            {
                if (!FileListBox.SelectedItems.Contains(_pressedFile)) { FileListBox.SelectedItems.Clear(); FileListBox.SelectedItems.Add(_pressedFile); }
                DragDrop.DoDragDrop(FileListBox, new DataObject(FilesFormat, FileListBox.SelectedItems.Cast<FileItem>().Select(f => f.Id).ToArray()), DragDropEffects.Move);
            }
            else if (_pressedCategory is not null) DragDrop.DoDragDrop(CategoryTree, new DataObject(CategoryFormat, _pressedCategory.Id), DragDropEffects.Move);
        }
        finally { _pressedFile = null; _pressedCategory = null; _deferSelection = false; }
    }
}
