using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Serilog;

namespace NexusExplorer.Services;

/// <summary>Disposable PNG cache, atomic writes, bounded storage. Never touches original media.</summary>
internal sealed class ThumbnailDiskCache(string directory, long budget)
{
    private readonly object _gate = new();
    private long _bytes;
    private int _count;
    private DateTime _nextScan;
    private string Name(string key) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
    internal ThumbnailResult? Read(string key)
    {
        lock (_gate)
        {
            try
            {
                Initialize();
                var name = Name(key);
                if (File.Exists(name + ".fail") && DateTime.UtcNow - File.GetLastWriteTimeUtc(name + ".fail") < TimeSpan.FromMinutes(2))
                    return new(null, ThumbnailStatus.Unavailable);
                var path = name + ".png";
                if (!File.Exists(path)) return null;
                try
                {
                    using var stream = File.OpenRead(path);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(1)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                    return new(bitmap, ThumbnailStatus.Ready);
                }
                catch { File.Delete(path); _nextScan = DateTime.MinValue; return null; }
            }
            catch (Exception ex) { Log.Debug(ex, "磁盘缩略图缓存不可读，将重新生成"); return null; }
        }
    }
    internal void Write(string key, ThumbnailResult result)
    {
        lock (_gate)
        {
            string? temporary = null;
            try
            {
                if (budget <= 0) return;
                Initialize();
                var path = Name(key) + (result.Image is null ? ".fail" : ".png");
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = File.Create(temporary))
                {
                    if (result.Image is not null)
                    {
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result.Image)); encoder.Save(stream);
                    }
                }
                var size = new FileInfo(temporary).Length;
                if (size > budget) return;
                if (File.Exists(path)) _bytes -= new FileInfo(path).Length; else _count++;
                File.Move(temporary, path, true); temporary = null; _bytes += size;
                Trim();
            }
            catch (Exception ex) { Log.Debug(ex, "磁盘缩略图缓存写入失败，仍使用内存结果"); }
            finally { if (temporary is not null) { try { File.Delete(temporary); } catch (Exception ex) { Log.Debug(ex, "临时缩略图缓存清理失败"); } } }
        }
    }
    private FileInfo[] Entries() => new DirectoryInfo(directory).GetFiles()
        .Where(f => f.Extension is ".png" or ".fail").ToArray();
    private void Initialize()
    {
        Directory.CreateDirectory(directory);
        if (DateTime.UtcNow < _nextScan) return;
        var entries = Entries(); _bytes = entries.Sum(f => f.Length); _count = entries.Length;
        _nextScan = DateTime.UtcNow.AddMinutes(5); Trim();
    }
    private void Trim()
    {
        if (_bytes <= budget && _count <= 32768) return;
        foreach (var file in Entries().OrderBy(f => f.LastWriteTimeUtc))
        {
            var size = file.Length; file.Delete(); _bytes -= size; _count--;
            if (_bytes <= budget && _count <= 32768) break;
        }
    }
}
