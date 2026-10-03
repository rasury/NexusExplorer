using NexusExplorer.ApplicationLayer;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public CategoryService Categories { get; }
    public FileService Files { get; }
    public OrganizationService Organization { get; }
    public PlaybackSession Session { get; } = new();
    [ObservableProperty] private Category? _currentCategory;
    [ObservableProperty] private FileItem? _currentFile;
    [ObservableProperty] private string _currentCategoryPath = "";
    [ObservableProperty] private ObservableCollection<FileItem> _currentFiles = new();
    public event Action? CategoryTreeChanged;
    public event Action? FileListChanged;
    public event Action<FileItem?>? CurrentFileChanged;
    public CategoryViewModel Category { get; }
    public FileListViewModel FileList { get; }
    public PlayerViewModel Player { get; }
    public NavigationViewModel Navigation { get; }
    private long _browseVersion;
    private long _activationVersion;

    public MainViewModel(CategoryService categories, FileService files, OrganizationService organization, IPlaybackEngine media)
    {
        Categories = categories; Files = files; Organization = organization;
        Category = new CategoryViewModel(this, categories);
        FileList = new FileListViewModel(this, categories, files);
        Player = new PlayerViewModel(this, files, media);
        Navigation = new NavigationViewModel(this, categories, files);
        var uiContext = SynchronizationContext.Current;
        Task StopOnUiAsync(IReadOnlyCollection<string> paths) => Infrastructure.UiDispatch.RunAsync(uiContext, () => StopForPathsAsync(paths));
        categories.BeforePhysicalOperationAsync = StopOnUiAsync;
        files.BeforePhysicalOperationAsync = StopOnUiAsync;
        organization.BeforePhysicalOperationAsync = paths => Infrastructure.UiDispatch.RunAsync(uiContext, async () =>
        { await RefreshOrganizationStatesAsync(); await StopForPathsAsync(paths); });
    }
    public async Task SelectCategoryAsync(Category? category)
    {
        CurrentCategory = category; await RefreshFilesAsync();
        var path = category is null ? "" : await Categories.GetCategoryPathAsync(category.Id);
        if (CurrentCategory?.Id == category?.Id) CurrentCategoryPath = path;
    }
    public async Task RefreshFilesAsync()
    {
        var version = Interlocked.Increment(ref _browseVersion); var id = CurrentCategory?.Id;
        var files = id is null ? new List<FileItem>() : await Files.GetByCategoryAsync(id.Value);
        if (version != Interlocked.Read(ref _browseVersion)) return;
        foreach (var f in files) f.IsCurrent = f.Id == CurrentFile?.Id;
        CurrentFiles = new ObservableCollection<FileItem>(files); FileListChanged?.Invoke();
    }
    public async Task RefreshTreeAsync()
    {
        await Category.LoadTreeAsync();
        if (CurrentCategory is not null)
        {
            CurrentCategory = await Categories.GetByIdAsync(CurrentCategory.Id);
            CurrentCategoryPath = CurrentCategory is null ? "" : await Categories.GetCategoryPathAsync(CurrentCategory.Id);
            await RefreshFilesAsync();
        }
        await Navigation.RefreshAfterTreeChangeAsync();
        CategoryTreeChanged?.Invoke();
    }
    public async Task RefreshOrganizationStatesAsync()
    {
        var states = await Categories.GetOrganizationStatesAsync();
        var visible = Category.FlatCategories.Concat(Navigation.Breadcrumb).Concat(Navigation.Children)
            .Concat(Navigation.PinnedCategories).Concat(new[] { CurrentCategory, Navigation.SelectedCategory }.OfType<Category>());
        foreach (var category in visible)
            if (states.TryGetValue(category.Id, out var organized)) category.IsOrganized = organized;
    }
    public async Task SelectFileAsync(FileItem? file)
    {
        if (file is not null)
            Session.Open(CurrentFiles.Where(f => PlayerViewModel.GetMediaKind(f.FileName) != MediaKind.Unsupported).Select(f => f.Id), file.Id);
        await ActivateQueuedAsync(file);
    }
    public async Task PlayQueuedIdAsync(int id)
    {
        var file = await Files.GetByIdAsync(id);
        if (file is not null && file.ExistsOnDisk) await ActivateQueuedAsync(file);
    }
    private async Task ActivateQueuedAsync(FileItem? file)
    {
        var version = Interlocked.Increment(ref _activationVersion);
        CurrentFile = file; if (file is not null) Session.SetCurrent(file.Id);
        foreach (var f in CurrentFiles) f.IsCurrent = f.Id == file?.Id;
        var category = file is null ? null : await Categories.GetByIdAsync(file.CategoryId);
        if (version != Interlocked.Read(ref _activationVersion)) return;
        await Navigation.OnCurrentFileChangedAsync(category);
        if (version != Interlocked.Read(ref _activationVersion)) return;
        await Player.PlayFileAsync(file);
        if (version == Interlocked.Read(ref _activationVersion)) CurrentFileChanged?.Invoke(file);
    }
    public async Task OnFileRecategorizedAsync() => await OnFilesRecategorizedAsync(CurrentFile is null ? Array.Empty<int>() : new[] { CurrentFile.Id });
    public async Task OnFilesRecategorizedAsync(IReadOnlyCollection<int> successfulIds)
    {
        var version = Interlocked.Read(ref _activationVersion); var currentId = CurrentFile?.Id;
        var advance = CurrentFile is not null && successfulIds.Contains(CurrentFile.Id);
        foreach (var id in successfulIds) Session.Classified.Add(id);
        if (currentId is int playingId)
        {
            var file = await Files.GetByIdAsync(playingId);
            if (version == Interlocked.Read(ref _activationVersion)) CurrentFile = file;
        }
        await RefreshOrganizationStatesAsync();
        await RefreshFilesAsync();
        if (advance && version == Interlocked.Read(ref _activationVersion) && !await PlayAdjacentAsync(1, classification: true)) await ActivateQueuedAsync(null);
    }
    public async Task StopForPathsAsync(IReadOnlyCollection<string> paths)
    {
        if (CurrentFile is null || !paths.Any(p => LocationService.IsWithin(CurrentFile.AbsolutePath, p))) return;
        await ActivateQueuedAsync(null);
    }
    public Task StopPlaybackForOrganizeAsync() => ActivateQueuedAsync(null);
    public async Task<int> GetCurrentFileCountAsync(int categoryId)
    {
        var count = 0;
        foreach (var c in await Categories.GetSubtreeAsync(categoryId)) count += (await Files.GetByCategoryAsync(c.Id)).Count;
        return count;
    }
    public async Task<bool> PlayAdjacentAsync(int offset, bool classification = false)
    {
        var version = Interlocked.Read(ref _activationVersion);
        while (Session.Step(offset, classification) is int id)
        {
            var file = await Files.GetByIdAsync(id);
            if (version != Interlocked.Read(ref _activationVersion)) return true; // A newer explicit selection owns playback.
            if (file is null || !file.ExistsOnDisk || PlayerViewModel.GetMediaKind(file.FileName) == MediaKind.Unsupported) continue;
            await ActivateQueuedAsync(file); return true;
        }
        return false;
    }
    public async Task ReplayQueueAsync(bool last = false)
    {
        foreach (var id in last ? Session.Queue.AsEnumerable().Reverse() : Session.Queue)
        {
            var f = await Files.GetByIdAsync(id); if (f is null || !f.ExistsOnDisk) continue;
            await ActivateQueuedAsync(f); break;
        }
    }
}
