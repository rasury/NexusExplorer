using System.IO;
using NexusExplorer.Services;

namespace NexusExplorer.ApplicationLayer;

/// <summary>Application entry point shared by tree drop and file-area import.</summary>
public sealed class ImportCoordinator(FileService files, CategoryService categories)
{
    public async Task<BatchAddResult> ImportAsync(IEnumerable<string> paths, int categoryId)
    {
        var combined = new BatchAddResult();
        foreach (var path in paths)
        {
            try
            {
                var result = Directory.Exists(path)
                    ? await files.ImportDirectoryAsync(path, categoryId, categories)
                    : await files.AddRangeAsync(new[] { path }, categoryId);
                combined.Added.AddRange(result.Added); combined.Failed.AddRange(result.Failed);
            }
            catch (Exception ex) { combined.Failed.Add((Path.GetFileName(path), ex.Message)); }
        }
        return combined;
    }
}
