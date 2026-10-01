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
            var result = await _fileService.ImportDirectoryAsync(directory, _main.CurrentCategory.Id);
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
        if (_main.CurrentCategory is null)
        {
            ShowError?.Invoke("请先选择一个分类,再拖入文件。");
            return;
        }

        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories));
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        if (files.Count == 0) return;

        var result = await _fileService.AddRangeAsync(files, _main.CurrentCategory.Id);
        await _main.RefreshFilesAsync();
        ReportBatchResult(result);
    }

    private void ReportBatchResult(BatchAddResult result)
    {
        if (result.Failed.Count == 0)
        {
            ShowInfo?.Invoke($"已添加 {result.Added.Count} 个文件到当前分类。");
        }
        else
        {
            var errors = string.Join("\n", result.Failed.Take(5).Select(f => $"• {f.FileName}: {f.Error}"));
            if (result.Failed.Count > 5)
                errors += $"\n… 以及另外 {result.Failed.Count - 5} 个失败";
            ShowError?.Invoke($"成功添加 {result.Added.Count} 个,失败 {result.Failed.Count} 个:\n{errors}");
        }
    }

    [RelayCommand]
    public async Task RemoveFileAsync(FileItem? file)
    {
        if (file is null) return;

        if (ShowConfirmDialog is not null &&
            !await ShowConfirmDialog($"确定把「{file.FileName}」从分类中移除?\n\n源文件保留在原位置,不会删除。"))
            return;

        try
        {
            await _fileService.RemoveAsync(file.Id);
            if (_main.CurrentFile?.Id == file.Id)
                await _main.SelectFileAsync(null);
            await _main.RefreshFilesAsync();
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError?.Invoke($"移除失败: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DeleteFileAsync(FileItem? file)
    {
        if (file is null) return;

        if (ShowConfirmDialog is not null &&
            !await ShowConfirmDialog($"确定删除文件「{file.FileName}」?\n文件将进入 Windows 回收站。"))
            return;

        try
        {
            await _fileService.DeleteAsync(file.Id, RecycleBin);
            if (_main.CurrentFile?.Id == file.Id)
                await _main.SelectFileAsync(null);
            await _main.RefreshFilesAsync();
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
            await _main.RefreshFilesAsync();
            ShowInfo?.Invoke($"已重新定位「{file.FileName}」。");
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
    }

    /// <summary>文件拖到分类树上某分类节点 → 重新分类。</summary>
    public async Task RecategorizeAsync(FileItem file, Category targetCategory)
    {
        try
        {
            await _fileService.RecategorizeAsync(file.Id, targetCategory.Id);
            await _main.OnFileRecategorizedAsync();
        }
        catch (OperationException ex)
        {
            ShowError?.Invoke(ex.Message);
        }
    }
}
