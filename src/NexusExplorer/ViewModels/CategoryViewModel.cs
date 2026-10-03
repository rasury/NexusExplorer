using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.ViewModels;

/// <summary>分类树左侧面板的 ViewModel。UI 交互由 View 层弹对话框处理。</summary>
public partial class CategoryViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CategoryService _categoryService;

    [ObservableProperty]
    private ObservableCollection<Category> _rootCategories = new();

    [ObservableProperty]
    private ObservableCollection<Category> _flatCategories = new();

    /// <summary>UI 弹出对话框的回调(由 View 注入,便于测试)。</summary>
    public Func<string, string?, Task<string?>>? ShowInputDialog { get; set; }

    /// <summary>UI 确认对话框回调。返回 true 表示确认。</summary>
    public Func<string, Task<bool>>? ShowConfirmDialog { get; set; }

    /// <summary>错误提示回调。</summary>
    public Action<string>? ShowError { get; set; }

    public IRecycleBinService RecycleBin { get; set; } = null!;

    public CategoryViewModel(MainViewModel main, CategoryService categoryService)
    {
        _main = main;
        _categoryService = categoryService;
    }

    public async Task LoadTreeAsync()
    {
        var states = FlatCategories.ToDictionary(c => c.Id, c => (c.IsExpanded, c.IsSelected));
        var tree = await _categoryService.GetTreeAsync();
        foreach (var c in Flatten(tree))
            if (states.TryGetValue(c.Id, out var state)) { c.IsExpanded = state.IsExpanded; c.IsSelected = state.IsSelected; }
        RootCategories = new ObservableCollection<Category>(tree);
        FlatCategories = new ObservableCollection<Category>(Flatten(tree));
    }

    private static IEnumerable<Category> Flatten(IEnumerable<Category> categories)
    {
        foreach (var category in categories)
        {
            yield return category;
            foreach (var child in Flatten(category.Children))
                yield return child;
        }
    }

    [RelayCommand]
    public async Task CreateRootAsync()
    {
        var name = await PromptNameAsync("新建分类", null);
        if (name is null) return;
        await CreateAsync(name, null);
    }

    public async Task CreateChildAsync(Category parent)
    {
        var name = await PromptNameAsync($"在「{parent.Name}」下新建分类", parent.Name);
        if (name is null) return;
        await CreateAsync(name, parent.Id);
    }

    private async Task CreateAsync(string name, int? parentId)
    {
        try
        {
            await _categoryService.CreateAsync(name.Trim(), parentId);
            await _main.RefreshTreeAsync();
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"创建分类失败: {ex.Message}");
        }
    }

    public async Task RenameAsync(Category category)
    {
        var name = await PromptNameAsync($"重命名「{category.Name}」", category.Name);
        if (name is null || name == category.Name) return;

        try
        {
            await _categoryService.RenameAsync(category.Id, name.Trim());
            await _main.RefreshTreeAsync();
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"重命名失败: {ex.Message}");
        }
    }

    public async Task RemoveAsync(Category category)
    {
        try
        {
            var (childCount, fileCount) = await _categoryService.GetRemovalSummaryAsync(category.Id);
            var message = $"确定移除分类「{category.Name}」？";
            if (ShowConfirmDialog is not null && !await ShowConfirmDialog(message)) return;
            var removedFileIds = await _categoryService.RemoveAsync(category.Id);
            if (_main.CurrentFile is { } current && removedFileIds.Contains(current.Id))
                await _main.SelectFileAsync(null);
            await _main.RefreshTreeAsync();
        }
        catch (Exception ex) { ShowError?.Invoke($"移除分类失败: {ex.Message}"); }
    }

    public async Task DeleteAsync(Category category)
    {
        var childCount = (await _categoryService.GetSubtreeAsync(category.Id)).Count - 1;
        var fileCount = (await _main.GetCurrentFileCountAsync(category.Id));

        var message = childCount > 0 || fileCount > 0
            ? $"确定删除分类「{category.Name}」?"
            : $"确定删除分类「{category.Name}」?";

        if (ShowConfirmDialog is not null && !await ShowConfirmDialog(message))
            return;

        try
        {
            await _categoryService.DeleteAsync(category.Id, RecycleBin);
            await _main.RefreshTreeAsync();
            if (_categoryService.LastDeleteWarnings.Count > 0)
                ShowError?.Invoke(string.Join("\n", _categoryService.LastDeleteWarnings));
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"删除失败: {ex.Message}");
        }
        finally { await _main.RefreshTreeAsync(); }
    }

    /// <summary>把分类移动到目标父分类(拖拽)。target = null 表示移到顶层。</summary>
    public async Task MoveAsync(Category category, Category? targetParent)
    {
        try
        {
            await _categoryService.MoveAsync(category.Id, targetParent?.Id);
            await _main.RefreshTreeAsync();
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"移动分类失败: {ex.Message}");
        }
    }

    /// <summary>钉到底栏快捷分类层。</summary>
    public async Task PinAsync(int categoryId)
    {
        try
        {
            await _categoryService.PinAsync(categoryId);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"钉住失败: {ex.Message}");
        }
    }

    /// <summary>从底栏快捷分类层取消。</summary>
    public async Task UnpinAsync(int categoryId)
    {
        try
        {
            await _categoryService.UnpinAsync(categoryId);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"取消钉住失败: {ex.Message}");
        }
    }

    public async Task MoveUpAsync(Category category) => await MoveOffsetAsync(category, -1);
    public async Task MoveDownAsync(Category category) => await MoveOffsetAsync(category, 1);

    private async Task MoveOffsetAsync(Category category, int offset)
    {
        try
        {
            await _categoryService.MoveWithinSiblingsAsync(category.Id, offset);
            await _main.RefreshTreeAsync();
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"调整排序失败: {ex.Message}");
        }
    }

    private async Task<string?> PromptNameAsync(string title, string? defaultValue)
    {
        if (ShowInputDialog is null)
            return defaultValue;
        return await ShowInputDialog(title, defaultValue);
    }
}
