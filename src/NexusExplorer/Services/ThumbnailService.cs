using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Serilog;

namespace NexusExplorer.Services;

internal enum ThumbnailStatus { Ready, Missing, Unavailable }
internal sealed record ThumbnailResult(BitmapSource? Image, ThumbnailStatus Status);

/// <summary>One lazy STA worker; metadata and extraction never run on the UI thread.</summary>
internal sealed class ThumbnailService : IDisposable
{
    private static readonly Lazy<ThumbnailService> SharedInstance = new(() => new ThumbnailService());
    internal static ThumbnailService Shared => SharedInstance.Value;
    internal static void ShutdownShared() { if (SharedInstance.IsValueCreated) SharedInstance.Value.Dispose(); }
    private sealed record Work(string Path, CancellationToken Token, TaskCompletionSource<ThumbnailResult> Completion);
    private sealed record Entry(string Path, long Length, long Modified, ThumbnailResult Result, long Bytes);
    private readonly BlockingCollection<Work> _queue = new(8);
    private readonly Func<string, BitmapSource?> _decode;
    private readonly long _budget;
    private readonly Dictionary<string, LinkedListNode<Entry>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recent = new();
    private long _cacheBytes;
    private int _disposed;
    internal long CacheBytes => Interlocked.Read(ref _cacheBytes);

    internal ThumbnailService(Func<string, BitmapSource?>? decoder = null, long cacheBudget = 32L * 1024 * 1024)
    {
        _decode = decoder ?? ThumbnailDecoder.Decode;
        _budget = Math.Max(0, cacheBudget);
        var worker = new Thread(Run) { IsBackground = true, Name = "Nexus thumbnail worker" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
    }

    internal async Task<ThumbnailResult> GetAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var completion = new TaskCompletionSource<ThumbnailResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new Work(path, token, completion);
        // Hover callers never wait synchronously, even when the bounded queue is full.
        await Task.Run(() => _queue.Add(work, token), token).ConfigureAwait(false);
        return await completion.Task.WaitAsync(token).ConfigureAwait(false);
    }

    private void Run()
    {
        var com = CoInitializeEx(IntPtr.Zero, 2);
        try
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                if (work.Token.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                { work.Completion.TrySetCanceled(); continue; }
                try
                {
                    var result = Read(work.Path);
                    if (work.Token.IsCancellationRequested) work.Completion.TrySetCanceled(work.Token);
                    else work.Completion.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "文件缩略图生成失败 {Path}", work.Path);
                    work.Completion.TrySetResult(new(null, ThumbnailStatus.Unavailable));
                }
            }
        }
        finally
        {
            _cache.Clear(); _recent.Clear(); Interlocked.Exchange(ref _cacheBytes, 0);
            if (com >= 0) CoUninitialize();
            _queue.Dispose();
        }
    }

    private ThumbnailResult Read(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists)
        {
            if (_cache.TryGetValue(info.FullName, out var missing)) Remove(missing);
            return new(null, ThumbnailStatus.Missing);
        }
        var length = info.Length; var modified = info.LastWriteTimeUtc.Ticks;
        if (_cache.TryGetValue(info.FullName, out var cached))
        {
            if (cached.Value.Length == length && cached.Value.Modified == modified)
            { _recent.Remove(cached); _recent.AddFirst(cached); return cached.Value.Result; }
            Remove(cached);
        }
        var bitmap = _decode(info.FullName);
        if (bitmap is not null && !bitmap.IsFrozen) bitmap.Freeze();
        var result = new ThumbnailResult(bitmap, bitmap is null ? ThumbnailStatus.Unavailable : ThumbnailStatus.Ready);
        var bytes = bitmap is null ? 0 : (long)((bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8) * bitmap.PixelHeight;
        if (bytes <= _budget)
        {
            var node = _recent.AddFirst(new Entry(info.FullName, length, modified, result, bytes));
            _cache[info.FullName] = node; Interlocked.Add(ref _cacheBytes, bytes);
            while (CacheBytes > _budget || _cache.Count > 256) Remove(_recent.Last!);
        }
        return result;
    }

    private void Remove(LinkedListNode<Entry> entry)
    { _cache.Remove(entry.Value.Path); _recent.Remove(entry); Interlocked.Add(ref _cacheBytes, -entry.Value.Bytes); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // An in-progress native extraction cannot be forcibly cancelled; never join it on the UI thread.
        _queue.CompleteAdding();
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
