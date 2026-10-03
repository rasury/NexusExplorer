using System.IO;
using System.Text.Json;
using Serilog;
namespace NexusExplorer.Infrastructure;
public sealed class UiStateStore
{
    public HashSet<int> Expanded { get; set; } = new();
    public int? SelectedCategoryId { get; set; }
    public int? BrowsedCategoryId { get; set; }
    public double TreeOffset { get; set; }
    public double FileOffset { get; set; }
    public static string PathName => Path.Combine(AppPaths.DataDirectory, "ui-state.json");
    public static UiStateStore Load()
    {
        try { return File.Exists(PathName) ? JsonSerializer.Deserialize<UiStateStore>(File.ReadAllText(PathName)) ?? new() : new(); }
        catch (Exception ex) { Log.Warning(ex, "读取界面状态失败"); return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var temporary = PathName + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, PathName, true);
        }
        catch (Exception ex) { Log.Warning(ex, "保存界面状态失败"); }
    }
}
