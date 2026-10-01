using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.ViewModels;

/// <summary>
/// 应用状态中枢:持有当前分类、当前文件,协调三个面板。
/// 左侧面板 → 设置 CurrentCategory/CurrentFile;
/// 右侧播放面板与底部导航栏 ← 响应变化。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly CategoryService _categoryService;
    private readonly FileService _fileService;

    [ObservableProperty]
    private Category? _currentCategory;

    [ObservableProperty]
    private FileItem? _currentFile;

    [ObservableProperty]
    private string _currentCategoryPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileItem> _currentFiles = new();

    /// <summary>分类树需要刷新时触发(左侧面板订阅)。</summary>
    public event Action? CategoryTreeChanged;

    /// <summary>文件列表需要刷新时触发。</summary>
    public event Action? FileListChanged;

    /// <summary>当前文件变化时触发(左侧面板同步列表选中)。</summary>
    public event Action<FileItem?>? CurrentFileChanged;

    public CategoryViewModel Category { get; }
    public FileListViewModel FileList { get; }
    public PlayerViewModel Player { get; }
    public NavigationViewModel Navigation { get; }

    public MainViewModel(
        CategoryService categoryService,
        FileService fileService,
        OrganizationService organizationService,
        MediaPlayerService mediaPlayerService)
    {
        _categoryService = categoryService;
        _fileService = fileService;

        Category = new CategoryViewModel(this, categoryService);
        FileList = new FileListViewModel(this, categoryService, _fileService);
        Player = new PlayerViewModel(this, _fileService, mediaPlayerService);
        Navigation = new NavigationViewModel(this, categoryService, _fileService);
    }

    // ---------- 当前分类 ----------

    public async Task SelectCategoryAsync(Category? category)
    {
        CurrentCategory = category;
        await RefreshFilesAsync();
        CurrentCategoryPath = category is null
            ? string.Empty
            : await _categoryService.GetCategoryPathAsync(category.Id);
    }

    public async Task RefreshFilesAsync()
    {
        CurrentFiles = CurrentCategory is null
            ? new ObservableCollection<FileItem>()
            : new ObservableCollection<FileItem>(await _fileService.GetByCategoryAsync(CurrentCategory.Id));
        FileListChanged?.Invoke();
    }

    public async Task RefreshTreeAsync()
    {
        await Category.LoadTreeAsync();
        await Navigation.RefreshPinnedAsync(); // 删除/移动分类后钉层同步
        // 当前分类可能已被删除/改名,重新取最新数据
        if (CurrentCategory is not null)
        {
            var latest = await _categoryService.GetByIdAsync(CurrentCategory.Id);
            if (latest is null)
            {
                CurrentCategory = null;
                CurrentCategoryPath = string.Empty;
                CurrentFiles = new ObservableCollection<FileItem>();
                FileListChanged?.Invoke();
            }
            else
            {
                CurrentCategoryPath = await _categoryService.GetCategoryPathAsync(latest.Id);
            }
        }
        CategoryTreeChanged?.Invoke();
    }

    // ---------- 当前文件 ----------

    public async Task SelectFileAsync(FileItem? file)
    {
        CurrentFile = file;
        if (file is not null)
        {
            // 底部导航栏显示当前文件的分类路径(而非当前浏览的分类)
            var category = await _categoryService.GetByIdAsync(file.CategoryId);
            CurrentCategoryPath = category is null
                ? "未分类"
                : await _categoryService.GetCategoryPathAsync(category.Id);
            await Navigation.OnCurrentFileChangedAsync(category);
        }
        else
        {
            await Navigation.OnCurrentFileChangedAsync(null);
        }
        await Player.PlayFileAsync(file);
        CurrentFileChanged?.Invoke(file);
    }

    /// <summary>文件被重新分类后刷新界面。</summary>
    public async Task OnFileRecategorizedAsync()
    {
        if (CurrentFile is not null)
        {
            var latest = await _fileService.GetByIdAsync(CurrentFile.Id);
            CurrentFile = latest;
            var category = latest is null ? null : await _categoryService.GetByIdAsync(latest.CategoryId);
            CurrentCategoryPath = category is null ? "未分类" : await _categoryService.GetCategoryPathAsync(category.Id);
            await Navigation.OnCurrentFileChangedAsync(category);
            await Navigation.RefreshChildrenAsync();
        }
        await RefreshFilesAsync();
    }

    /// <summary>
    /// 整理前停止播放:正在播放的文件被 VLC 占用会导致 File.Move 失败
    /// ("being used by another process")。清空当前文件引用但保留文件列表,
    /// 整理完成后由 RefreshFilesAsync 重建。
    /// </summary>
    public async Task StopPlaybackForOrganizeAsync()
    {
        if (CurrentFile is null) return;

        // 停止 VLC(释放文件句柄)并清空播放面板;
        // 底部导航的路径显示回退到当前浏览分类
        await Player.PlayFileAsync(null);
        CurrentFile = null;
        CurrentCategoryPath = CurrentCategory is null
            ? string.Empty
            : await _categoryService.GetCategoryPathAsync(CurrentCategory.Id);
    }

    /// <summary>统计某分类子树内的文件总数(删除确认提示用)。</summary>
    public async Task<int> GetCurrentFileCountAsync(int categoryId)
    {
        var subtree = await _categoryService.GetSubtreeAsync(categoryId);
        var count = 0;
        foreach (var category in subtree)
            count += (await _fileService.GetByCategoryAsync(category.Id)).Count;
        return count;
    }

    /// <summary>播放列表内上一项/下一项。返回是否成功移动(越界返回 false)。</summary>
    public async Task<bool> PlayAdjacentAsync(int offset)
    {
        if (CurrentFile is null || CurrentFiles.Count == 0) return false;

        var index = -1;
        for (var i = 0; i < CurrentFiles.Count; i++)
        {
            if (CurrentFiles[i].Id == CurrentFile.Id)
            {
                index = i;
                break;
            }
        }

        var next = index + offset;
        if (next < 0 || next >= CurrentFiles.Count) return false;

        await SelectFileAsync(CurrentFiles[next]);
        return true;
    }
}
