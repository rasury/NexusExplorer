using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.ViewModels;

/// <summary>
/// 底部导航栏交互模型:
/// - 点击分类 = 纯导航(展开下一层子分类),不改变文件归属
/// - 绿色打勾按钮 = 把当前文件归入"选中的分类"
/// - 文件已属于选中分类时,打勾按钮禁用
/// - 打开文件时面包屑预展开到该文件所属分类(便于继续下钻子分类)
/// </summary>
public partial class NavigationViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CategoryService _categoryService;
    private readonly FileService _fileService;

    [ObservableProperty]
    private string _currentPathText = "未分类";

    /// <summary>面包屑:从第一层到当前导航位置的分类链。</summary>
    [ObservableProperty]
    private ObservableCollection<Category> _breadcrumb = new();

    /// <summary>当前层显示的子分类(点击导航下钻)。</summary>
    [ObservableProperty]
    private ObservableCollection<Category> _children = new();

    /// <summary>当前导航选中的分类(打勾按钮的目标);根层 = null。</summary>
    [ObservableProperty]
    private Category? _selectedCategory;

    /// <summary>选中分类是否就是当前文件所属分类(打勾按钮置灰条件)。</summary>
    [ObservableProperty]
    private bool _isSelectedCategoryCurrent;

    /// <summary>是否有当前播放文件(无文件时整条导航禁用)。</summary>
    [ObservableProperty]
    private bool _hasCurrentFile;

    /// <summary>是否位于根层(显示第一层分类,未选中任何分类)。</summary>
    [ObservableProperty]
    private bool _isAtRoot = true;

    /// <summary>底栏第二层:用户钉的快捷分类。</summary>
    [ObservableProperty]
    private ObservableCollection<Category> _pinnedCategories = new();

    private int? _currentFileCategoryId;
    private long _version;

    public Action<string>? ShowError { get; set; }

    public NavigationViewModel(MainViewModel main, CategoryService categoryService, FileService fileService)
    {
        _main = main;
        _categoryService = categoryService;
        _fileService = fileService;
    }

    /// <summary>当前文件变化:刷新路径显示,面包屑展开到该文件所属分类链。</summary>
    public async Task OnCurrentFileChangedAsync(Category? category)
    {
        var version = Interlocked.Increment(ref _version);
        var path = category is null ? "未分类" : await _categoryService.GetCategoryPathAsync(category.Id);
        var chain = category is null ? new List<Category>() : await GetAncestorChainAsync(category.Id);
        var children = await _categoryService.GetChildrenAsync(category?.Id);
        var pinned = await _categoryService.GetPinnedAsync();
        if (version != Interlocked.Read(ref _version)) return;
        _currentFileCategoryId = category?.Id;
        HasCurrentFile = category is not null;
        CurrentPathText = path; Breadcrumb = new(chain); Children = new(children); PinnedCategories = new(pinned);
        IsAtRoot = chain.Count == 0; UpdateSelectionState();
    }

    /// <summary>从根到指定分类的祖先链(含自身)。</summary>
    private async Task<List<Category>> GetAncestorChainAsync(int categoryId)
    {
        // 逐级上溯到根
        var chain = new List<Category>();
        var seen = new HashSet<int>();
        var current = await _categoryService.GetByIdAsync(categoryId);
        while (current is not null)
        {
            if (!seen.Add(current.Id)) throw new OperationException("分类结构存在循环。");
            chain.Insert(0, current);
            current = current.ParentId is null
                ? null
                : await _categoryService.GetByIdAsync(current.ParentId.Value);
        }
        return chain;
    }

    /// <summary>刷新底栏第二层的快捷分类。</summary>
    public async Task RefreshPinnedAsync()
    {
        var pinned = await _categoryService.GetPinnedAsync();
        PinnedCategories = new ObservableCollection<Category>(pinned);
        foreach (var c in PinnedCategories) c.IsSelected = c.Id == SelectedCategory?.Id;
    }

    public async Task RefreshAfterTreeChangeAsync()
    {
        var selectedId = SelectedCategory?.Id;
        await RefreshPinnedAsync();
        var selected = selectedId is null ? null : await _categoryService.GetByIdAsync(selectedId.Value);
        Breadcrumb = selected is null ? new() : new(await GetAncestorChainAsync(selected.Id));
        if (_main.CurrentFile is not null)
            CurrentPathText = await _categoryService.GetCategoryPathAsync(_main.CurrentFile.CategoryId);
        await RefreshChildrenAsync();
    }

    /// <summary>钉/取消钉后由 UI 调用:刷新钉层并保持选中态。</summary>
    public async Task OnPinsChangedAsync()
    {
        await RefreshPinnedAsync();
        UpdateSelectionState();
    }

    /// <summary>点击快捷分类:选中它(与导航层选中互斥,共用绿勾)。</summary>
    public async Task SelectPinnedAsync(Category category)
    {
        var version = Interlocked.Increment(ref _version);
        // 快捷分类可能位于任意层级;面包屑同步展开到它的链(与导航层一致)
        var chain = await GetAncestorChainAsync(category.Id);
        if (version != Interlocked.Read(ref _version)) return;
        Breadcrumb = new ObservableCollection<Category>(chain);
        await RefreshChildrenAsync();
    }

    /// <summary>刷新当前层的子分类列表。</summary>
    public async Task RefreshChildrenAsync()
    {
        var version = Interlocked.Read(ref _version);
        int? parentId = Breadcrumb.Count == 0 ? null : Breadcrumb[^1].Id;
        var children = await _categoryService.GetChildrenAsync(parentId);
        if (version != Interlocked.Read(ref _version)) return;
        Children = new ObservableCollection<Category>(children);
        IsAtRoot = Breadcrumb.Count == 0;
        UpdateSelectionState();
    }

    /// <summary>点击分类:纯导航,下钻一层显示其子分类;同时作为选中分类。</summary>
    public async Task NavigateToAsync(Category category)
    {
        Interlocked.Increment(ref _version);
        // 同级切换:替换面包屑末级;否则下钻
        if (Breadcrumb.Count > 0 && Breadcrumb[^1].ParentId == category.ParentId)
            Breadcrumb[^1] = category;
        else
            Breadcrumb.Add(category);

        SelectedCategory = category;
        await RefreshChildrenAsync();
    }

    /// <summary>面包屑回退到某层(index = 0..n-1);-1 表示回到根层。</summary>
    public async Task NavigateBackToAsync(int index)
    {
        Interlocked.Increment(ref _version);
        if (index < 0)
        {
            Breadcrumb = new ObservableCollection<Category>();
        }
        else
        {
            while (Breadcrumb.Count > index + 1)
                Breadcrumb.RemoveAt(Breadcrumb.Count - 1);
        }

        SelectedCategory = Breadcrumb.Count > 0 ? Breadcrumb[^1] : null;
        await RefreshChildrenAsync();
    }

    private void UpdateSelectionState()
    {
        SelectedCategory = Breadcrumb.Count > 0 ? Breadcrumb[^1] : null;
        IsSelectedCategoryCurrent = SelectedCategory is not null
            && _currentFileCategoryId == SelectedCategory.Id;
        foreach (var c in PinnedCategories) c.IsSelected = c.Id == SelectedCategory?.Id;
    }

    /// <summary>打勾按钮:把当前文件归入选中的分类。</summary>
    public async Task ConfirmRecategorizeAsync()
    {
        if (_main.CurrentFile is null)
        {
            ShowError?.Invoke("当前没有打开的文件。");
            return;
        }

        if (SelectedCategory is null)
        {
            ShowError?.Invoke("请先选择一个分类。");
            return;
        }

        if (IsSelectedCategoryCurrent)
            return; // 已属于该分类

        try
        {
            await _main.FileList.RecategorizeManyAsync(new[] { _main.CurrentFile }, SelectedCategory);
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
    }
}
