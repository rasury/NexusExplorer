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
        var tree = await _categoryService.GetTreeAsync();
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

    public async Task DeleteAsync(Category category)
    {
        var childCount = (await _categoryService.GetSubtreeAsync(category.Id)).Count - 1;
        var fileCount = (await _main.GetCurrentFileCountAsync(category.Id));

        var message = childCount > 0 || fileCount > 0
            ? $"确定删除分类「{category.Name}」?\n\n包含 {childCount} 个子分类、{fileCount} 个文件。\n分类下所有文件和物理目录将进入回收站。"
            : $"确定删除分类「{category.Name}」?\n对应的物理目录将进入回收站。";

        if (ShowConfirmDialog is not null && !await ShowConfirmDialog(message))
            return;

        try
        {
            await _categoryService.DeleteAsync(category.Id, RecycleBin);
            await _main.RefreshTreeAsync();
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"删除失败: {ex.Message}");
        }
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
