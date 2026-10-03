using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusExplorer.Models;
using NexusExplorer.Services;

namespace NexusExplorer.ViewModels;

/// <summary>左侧下半部分:当前分类的文件列表。</summary>
public partial class FileListViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CategoryService _categoryService;
    private readonly FileService _fileService;

    [ObservableProperty]
    private string _headerText = "当前分类";

    public ObservableCollection<FileItem> Files => _main.CurrentFiles;

    public IRecycleBinService RecycleBin { get; set; } = null!;

    /// <summary>选择文件(播放)回调。</summary>
    public Func<FileItem, Task>? OnFileActivated { get; set; }

    /// <summary>添加文件对话框(多选)。由 View 注入。</summary>
    public Func<Task<IReadOnlyList<string>>>? PickFiles { get; set; }

    /// <summary>添加文件夹对话框。由 View 注入。</summary>
    public Func<Task<string?>>? PickDirectory { get; set; }

    /// <summary>重新定位文件对话框。返回新路径或 null。</summary>
    public Func<string, Task<string?>>? PickRelocateFile { get; set; }

    public Action<string>? ShowError { get; set; }
    public Func<string, Task<bool>>? ShowConfirmDialog { get; set; }
    public Action<string>? ShowInfo { get; set; }
    public Action<string>? ShowImportStatus { get; set; }

    public FileListViewModel(MainViewModel main, CategoryService categoryService, FileService fileService)
    {
        _main = main;
        _categoryService = categoryService;
        _fileService = fileService;
    }

    public void UpdateHeader()
    {
        HeaderText = _main.CurrentCategory is null
            ? "当前分类"
            : $"当前分类:{_main.CurrentCategory.Name}";
    }

    [RelayCommand]
    public async Task ActivateFileAsync(FileItem? file)
    {
        if (file is null) return;
        if (!file.ExistsOnDisk)
        {
            ShowError?.Invoke($"文件已失效(可能被移动或删除):\n{file.AbsolutePath}\n\n请右键选择「重新定位」。");
            return;
        }
        await _main.SelectFileAsync(file);
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        if (_main.CurrentCategory is null)
        {
            ShowError?.Invoke("请先选择一个分类。");
            return;
        }

        if (PickFiles is null) return;
        var paths = await PickFiles();
        if (paths.Count == 0) return;

        var result = await _fileService.AddRangeAsync(paths, _main.CurrentCategory.Id);
        await _main.RefreshOrganizationStatesAsync();
        await _main.RefreshFilesAsync();
        ReportBatchResult(result);
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        if (_main.CurrentCategory is null)
        {
            ShowError?.Invoke("请先选择一个分类。");
            return;
        }

        if (PickDirectory is null) return;
        var directory = await PickDirectory();
        if (string.IsNullOrEmpty(directory)) return;

        try
        {
            var result = await _fileService.ImportDirectoryAsync(directory, _main.CurrentCategory.Id, _categoryService);
            await _main.RefreshTreeAsync();
            await _main.RefreshFilesAsync();
            ReportBatchResult(result);
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
    }

    /// <summary>Windows Explorer 拖入:文件或文件夹。</summary>
    public async Task ImportDroppedPathsAsync(string[] paths)
    {
        if (_main.CurrentCategory is null) { ShowError?.Invoke("请先双击打开分类，再拖入文件。"); return; }
        await ImportIntoAsync(paths, _main.CurrentCategory);
    }

    public async Task ImportIntoAsync(string[] paths, Category target)
    {
        var coordinator = new NexusExplorer.ApplicationLayer.ImportCoordinator(_fileService, _categoryService);
        var result = await coordinator.ImportAsync(paths, target.Id);
        await _main.RefreshTreeAsync(); ReportBatchResult(result, nonBlocking: true);
    }

    private void ReportBatchResult(BatchAddResult result, bool nonBlocking = false)
    {
        if (result.Failed.Count == 0)
        {
            (nonBlocking ? ShowImportStatus : ShowInfo)?.Invoke($"已添加 {result.Added.Count} 个文件。");
        }
        else
        {
            var errors = string.Join("\n", result.Failed.Take(5).Select(f => $"• {f.FileName}: {f.Error}"));
            if (result.Failed.Count > 5)
                errors += $"\n… 以及另外 {result.Failed.Count - 5} 个失败";
            (nonBlocking ? ShowImportStatus : ShowError)?.Invoke($"成功添加 {result.Added.Count} 个,失败 {result.Failed.Count} 个:\n{errors}");
        }
    }

    [RelayCommand]
    public Task RemoveFileAsync(FileItem? file) => DeleteManyAsync(file is null ? Array.Empty<FileItem>() : new[] { file }, true);
    [RelayCommand]
    public Task DeleteFileAsync(FileItem? file) => DeleteManyAsync(file is null ? Array.Empty<FileItem>() : new[] { file }, false);

    [RelayCommand]
    public async Task RelocateFileAsync(FileItem? file)
    {
        if (file is null) return;
        if (PickRelocateFile is null) return;

        var newPath = await PickRelocateFile(file.FileName);
        if (string.IsNullOrEmpty(newPath)) return;

        try
        {
            await _fileService.RelocateAsync(file.Id, newPath);
            await _main.RefreshOrganizationStatesAsync();
            await _main.RefreshFilesAsync();
            ShowInfo?.Invoke($"已重新定位「{file.FileName}」。");
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
    }

    public async Task RecategorizeManyAsync(IReadOnlyList<FileItem> files, Category target)
    {
        var succeeded = new List<int>(); var errors = new List<string>();
        foreach (var f in files)
        {
            if (f.CategoryId == target.Id) continue;
            try { await _fileService.RecategorizeAsync(f.Id, target.Id); succeeded.Add(f.Id); }
            catch (Exception ex) { errors.Add($"{f.FileName}: {ex.Message}"); Serilog.Log.Error(ex, "批量归类失败"); }
        }
        await _main.OnFilesRecategorizedAsync(succeeded);
        if (errors.Count > 0) ShowError?.Invoke($"成功 {succeeded.Count}，失败 {errors.Count}\n" + string.Join("\n", errors));
    }
    public Task RecategorizeAsync(FileItem file, Category target) => RecategorizeManyAsync(new[] { file }, target);

    public async Task DeleteManyAsync(IReadOnlyList<FileItem> files, bool removeOnly)
    {
        if (files.Count == 0) return;
        if (ShowConfirmDialog is not null && !await ShowConfirmDialog(removeOnly
            ? $"从分类移除 {files.Count} 个文件？"
            : $"删除 {files.Count} 个文件？")) return;
        var failures = new List<string>(); var succeeded = 0;
        foreach (var file in files)
        {
            try
            {
                if (_main.CurrentFile?.Id == file.Id) await _main.SelectFileAsync(null);
                if (removeOnly) await _fileService.RemoveAsync(file.Id);
                else await _fileService.DeleteAsync(file.Id, RecycleBin);
                succeeded++;
            }
            catch (Exception ex) { failures.Add($"{file.FileName}: {ex.Message}"); Serilog.Log.Error(ex, "批量文件操作失败"); }
        }
        await _main.RefreshOrganizationStatesAsync();
        await _main.RefreshFilesAsync();
        if (failures.Count > 0) ShowError?.Invoke($"成功 {succeeded}，失败 {failures.Count}\n" + string.Join("\n", failures));
    }
}
