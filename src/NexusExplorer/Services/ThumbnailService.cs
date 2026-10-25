using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using NexusExplorer.Infrastructure;
using Serilog;

namespace NexusExplorer.Services;
internal enum ThumbnailStatus { Ready, Missing, Unavailable }
internal enum ThumbnailPriority { Prefetch, Visible, Hover }
internal sealed record ThumbnailResult(BitmapSource? Image, ThumbnailStatus Status);

/// <summary>Cache reads bypass the single STA extraction worker; _gate protects shared state.</summary>
internal sealed class ThumbnailService : IDisposable
{
    private static readonly Lazy<ThumbnailService> SharedInstance = new(() => new ThumbnailService(diskDirectory: AppPaths.ThumbnailCacheDirectory));
    internal static ThumbnailService Shared => SharedInstance.Value;
    internal static void ShutdownShared() { if (SharedInstance.IsValueCreated) SharedInstance.Value.Dispose(); }
    private sealed class Work(string path, string key, string? identity, ThumbnailPriority priority)
    {
        internal readonly string Path = path, Key = key;
        internal readonly string? Identity = identity;
        internal ThumbnailPriority Priority = priority;
        internal int Waiters;
        internal readonly TaskCompletionSource<ThumbnailResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record Entry(string Key, ThumbnailResult Result, long Bytes, DateTime Expires);
    private readonly object _gate = new();
    private readonly List<Work> _pending = new();
    private readonly Dictionary<string, Work> _flights = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _cache = new();
    private readonly LinkedList<Entry> _recent = new();
    private readonly SemaphoreSlim _reads = new(2);
    private readonly Func<string, BitmapSource?> _decode;
    private readonly ThumbnailDiskCache? _disk;
    private readonly long _budget;
    private long _cacheBytes;
    private bool _disposed;
    internal long CacheBytes { get { lock (_gate) return _cacheBytes; } }
    internal int PendingCount { get { lock (_gate) return _pending.Count; } }

    internal ThumbnailService(Func<string, BitmapSource?>? decoder = null, long cacheBudget = 128L * 1024 * 1024,
        string? diskDirectory = null, long diskBudget = 512L * 1024 * 1024)
    {
        _decode = decoder ?? ThumbnailDecoder.Decode; _budget = Math.Max(0, cacheBudget);
        if (diskDirectory is not null) _disk = new ThumbnailDiskCache(diskDirectory, diskBudget);
        var thread = new Thread(Run) { IsBackground = true, Name = "Nexus thumbnail extraction" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }

    internal async Task<ThumbnailResult> GetAsync(string path, CancellationToken token = default,
        ThumbnailPriority priority = ThumbnailPriority.Visible, string? identity = null)
    {
        token.ThrowIfCancellationRequested();
        var version = await Task.Run(() => Version(path, identity), token).ConfigureAwait(false);
        Work work; bool start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (version is null) return new(null, ThumbnailStatus.Missing);
            if (_cache.TryGetValue(version, out var cached))
            {
                if (cached.Value.Expires > DateTime.UtcNow)
                { _recent.Remove(cached); _recent.AddFirst(cached); return cached.Value.Result; }
                Remove(cached);
            }
            start = !_flights.TryGetValue(version, out work!);
            if (start) { work = new Work(path, version, identity, priority); _flights.Add(version, work); }
            work.Waiters++; work.Priority = (ThumbnailPriority)Math.Max((int)priority, (int)work.Priority);
        }
        if (start) _ = ReadOrQueueAsync(work);
        try { return await work.Completion.Task.WaitAsync(token).ConfigureAwait(false); }
        finally { lock (_gate) work.Waiters--; }
    }

    private static string? Version(string path, string? identity)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists) return null;
        return $"v1|{identity ?? info.FullName.ToUpperInvariant()}|{info.Extension.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{ThumbnailDecoder.MaxEdge}";
    }

    private async Task ReadOrQueueAsync(Work work)
    {
        try
        {
            await _reads.WaitAsync().ConfigureAwait(false);
            ThumbnailResult? result;
            try
            {
                lock (_gate) { if (_disposed || work.Waiters == 0) { Complete(work, null); return; } }
                result = _disk is null ? null : await Task.Run(() => _disk.Read(work.Key)).ConfigureAwait(false);
            }
            finally { _reads.Release(); }
            lock (_gate)
            {
                if (_disposed || work.Waiters == 0) { Complete(work, null); return; }
                if (result is not null) { Complete(work, result); return; }
                if (_pending.Count >= 64)
                {
                    var lowest = _pending.OrderBy(w => w.Priority).First();
                    if (lowest.Priority > work.Priority) { Complete(work, null); return; }
                    _pending.Remove(lowest); Complete(lowest, null);
                }
                _pending.Add(work); Monitor.Pulse(_gate);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "缩略图缓存读取失败"); lock (_gate) Complete(work, null); }
    }

    private void Run()
    {
        var com = CoInitializeEx(IntPtr.Zero, 2);
        try
        {
            while (true)
            {
                Work work;
                lock (_gate)
                {
                    while (!_disposed && _pending.Count == 0) Monitor.Wait(_gate);
                    if (_disposed) return;
                    work = _pending.OrderByDescending(w => w.Priority).First(); _pending.Remove(work);
                    if (work.Waiters == 0) { Complete(work, null); continue; }
                }
                ThumbnailResult result;
                try
                {
                    var image = _decode(work.Path);
                    if (image is not null && !image.IsFrozen) image.Freeze();
                    result = new(image, image is null ? ThumbnailStatus.Unavailable : ThumbnailStatus.Ready);
                    if (Version(work.Path, work.Identity) != work.Key) { lock (_gate) Complete(work, null); continue; }
                }
                catch (Exception ex) { Log.Debug(ex, "文件缩略图生成失败 {Path}", work.Path); result = new(null, ThumbnailStatus.Unavailable); }
                lock (_gate) { if (_disposed) return; }
                _disk?.Write(work.Key, result);
                lock (_gate) Complete(work, result);
            }
        }
        finally { if (com >= 0) CoUninitialize(); }
    }

    // Null means cancellation/backpressure, not a negative cache entry.
    private void Complete(Work work, ThumbnailResult? result)
    {
        _flights.Remove(work.Key);
        if (!_disposed && result is not null)
        {
            var image = result.Image;
            var bytes = image is null ? 64 : (long)((image.PixelWidth * image.Format.BitsPerPixel + 7) / 8) * image.PixelHeight;
            if (bytes <= _budget)
            {
                if (_cache.TryGetValue(work.Key, out var old)) Remove(old);
                var node = _recent.AddFirst(new Entry(work.Key, result, bytes,
                    image is null ? DateTime.UtcNow.AddMinutes(2) : DateTime.MaxValue));
                _cache[work.Key] = node; _cacheBytes += bytes;
                while (_cacheBytes > _budget || _cache.Count > 8192) Remove(_recent.Last!);
            }
        }
        work.Completion.TrySetResult(result ?? new(null, ThumbnailStatus.Unavailable));
    }
    private void Remove(LinkedListNode<Entry> entry)
    { _cache.Remove(entry.Value.Key); _recent.Remove(entry); _cacheBytes -= entry.Value.Bytes; }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var work in _flights.Values) work.Completion.TrySetCanceled();
            _flights.Clear(); _pending.Clear(); _cache.Clear(); _recent.Clear(); _cacheBytes = 0;
            Monitor.PulseAll(_gate);
        }
        // Native extraction cannot be interrupted safely; never join it on the UI thread.
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
