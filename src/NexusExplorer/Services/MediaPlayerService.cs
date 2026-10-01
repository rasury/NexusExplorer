using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Serilog;
using LibVLCMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace NexusExplorer.Services;

/// <summary>播放列表循环模式。</summary>
public enum PlayMode
{
    Sequential,   // 顺序播放(播完停止)
    RepeatAll,    // 列表循环
    RepeatOne,    // 单曲循环
    Shuffle       // 随机
}

/// <summary>
/// LibVLC 播放服务:管理 LibVLC/MediaPlayer 生命周期、播放控制、播放列表。
///
/// 视频输出不使用 VideoView(HwndHost 原生窗口会浮在所有 WPF 内容之上,
/// 造成"空域"问题:遮挡左侧面板、遮挡控制条)。改为 libvlc_video_set_callbacks
/// 把解码帧复制到 WriteableBitmap,视频成为普通 WPF 元素。
///
/// 帧数据流:VLC 线程(lock 回调写入非托管缓冲)→ display 回调在锁内复制到
/// 托管数组 → UI 线程 WritePixels。位图不能 Freeze(Frozen 位图不可写)。
/// 自动前进/上一项/下一项由 UI 层驱动(保证左侧列表选中同步),服务只上报
/// MediaEnded 事件。
/// </summary>
public class MediaPlayerService : IDisposable
{
    private readonly Random _random = new();
    private readonly Dispatcher _dispatcher;
    private readonly Lazy<LibVLC> _libVlcLazy;
    private readonly Lazy<LibVLCMediaPlayer> _playerLazy;

    /// <summary>LibVLC 实例(首次访问时初始化,可能耗时:加载全部插件)。</summary>
    public LibVLC LibVLC => _libVlcLazy.Value;

    /// <summary>VLC 播放器。</summary>
    public LibVLCMediaPlayer Player => _playerLazy.Value;

    /// <summary>VLC 是否已初始化完成(未完成时 UI 轮询/控制应跳过)。</summary>
    public bool IsVlcReady => _libVlcLazy.IsValueCreated && _playerLazy.IsValueCreated;

    /// <summary>当前播放列表(文件路径)。</summary>
    public IList<string> Playlist { get; } = new List<string>();

    public int CurrentIndex { get; private set; } = -1;

    public PlayMode Mode { get; set; } = PlayMode.Sequential;

    public string? CurrentPath { get; private set; }

    // ---------- 视频帧输出 ----------

    // volatile:display 回调(VLC 线程)读,UI 线程创建赋值,保证跨线程可见
    private volatile WriteableBitmap? _videoBitmap;
    private IntPtr _videoBuffer = IntPtr.Zero;
    private int _frameStride;
    private int _frameHeight;
    private byte[]? _pendingFrame;
    private bool _framePending;
    private readonly object _videoLock = new();

    /// <summary>视频帧位图(供 UI 显示)。无视频时为 null。仅在 UI 线程访问其成员。</summary>
    public WriteableBitmap? VideoBitmap => _videoBitmap;

    /// <summary>视频位图创建/销毁时触发(UI 线程)。</summary>
    public event Action? VideoSizeChanged;

    /// <summary>当前媒体播放完毕(VLC 线程触发)。前进策略由 UI 层决定。</summary>
    public event Action? MediaEnded;

    public event Action<string, string>? PlaybackError;

    // VLC 回调委托必须保活,否则被 GC 后原生回调崩溃
    private readonly LibVLCMediaPlayer.LibVLCVideoFormatCb _formatCb;
    private readonly LibVLCMediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    private readonly LibVLCMediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly LibVLCMediaPlayer.LibVLCVideoUnlockCb _unlockCb;
    private readonly LibVLCMediaPlayer.LibVLCVideoDisplayCb _displayCb;

    /// <summary>
    /// dispatcher = 拥有 WPF 位图的 UI 线程调度器。显式传入,
    /// 不依赖 Application.Current(测试环境它可能是别的线程的)。
    /// </summary>
    public MediaPlayerService(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher
            ?? System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        _formatCb = VideoFormatCallback;
        _cleanupCb = VideoCleanupCallback;
        _lockCb = VideoLockCallback;
        _unlockCb = VideoUnlockCallback;
        _displayCb = VideoDisplayCallback;

        _libVlcLazy = new Lazy<LibVLC>(() =>
        {
            Log.Information("初始化 LibVLC…");
            var libVlc = new LibVLC("--no-osd");
            Log.Information("LibVLC 初始化完成");
            return libVlc;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        _playerLazy = new Lazy<LibVLCMediaPlayer>(() =>
        {
            var player = new LibVLCMediaPlayer(LibVLC)
            {
                EnableHardwareDecoding = false // 回调模式用软件解码,保证帧可读
            };
            player.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
            player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
            player.EndReached += OnEndReached;
            player.EncounteredError += OnEncounteredError;
            return player;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        // 后台预热:冷启动时 VLC 加载几百个插件可能要几十秒,
        // 提前在后台线程开始,主窗口构造不被阻塞
        Task.Run(() =>
        {
            try { _ = Player; }
            catch (Exception ex) { Log.Error(ex, "LibVLC 后台预热失败"); }
        });
    }

    // ---------- 播放控制 ----------

    /// <summary>设置播放列表并播放指定索引。playlist 为 null 时保留现有列表。</summary>
    public void Play(IReadOnlyList<string>? playlist, int index = 0)
    {
        if (playlist is not null)
        {
            Playlist.Clear();
            foreach (var item in playlist)
                Playlist.Add(item);
        }

        if (Playlist.Count == 0)
            return;

        index = Math.Clamp(index, 0, Playlist.Count - 1);
        PlayAt(index);
    }

    public void PlayAt(int index)
    {
        if (index < 0 || index >= Playlist.Count)
            return;

        CurrentIndex = index;
        CurrentPath = Playlist[index];

        try
        {
            using var media = new Media(LibVLC, new Uri(CurrentPath));
            Player.Play(media);
            Log.Debug("开始播放: {Path}", CurrentPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "无法播放: {Path}", CurrentPath);
            PlaybackError?.Invoke(CurrentPath, ex.Message);
        }
    }

    /// <summary>重播当前曲目(停止/播完后 VLC 的 Pause 无效,必须重新 Play)。</summary>
    public void Replay()
    {
        if (CurrentIndex >= 0 && CurrentIndex < Playlist.Count)
            PlayAt(CurrentIndex);
    }

    public void PlayPause() => Player.Pause();

    public void Stop()
    {
        if (!IsVlcReady) return;
        Player.Stop();
        CurrentPath = null;
        // 保留 Playlist/CurrentIndex 供 Replay 使用

        // 立即清掉显示位图(不等 cleanup 回调,避免与下一次播放竞态)
        _dispatcher.BeginInvoke(() =>
        {
            lock (_videoLock)
            {
                _videoBitmap = null;
                _pendingFrame = null;
                _framePending = false;
            }
            VideoSizeChanged?.Invoke();
        });
    }

    // ---------- VLC 视频回调(VLC 线程) ----------

    private uint VideoFormatCallback(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        FormatCallbackCount++;

        // RV32 = 32 位打包 RGB(小端内存序 B,G,R,X,与 WPF Bgra32 一致)
        Marshal.WriteByte(chroma, 0, (byte)'R');
        Marshal.WriteByte(chroma, 1, (byte)'V');
        Marshal.WriteByte(chroma, 2, (byte)'3');
        Marshal.WriteByte(chroma, 3, (byte)'2');

        lock (_videoLock)
        {
            var stride = (int)width * 4;
            _frameStride = stride;
            _frameHeight = (int)height;
            _pendingFrame = new byte[stride * (int)height];
            _framePending = false;

            if (_videoBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(_videoBuffer);
            _videoBuffer = Marshal.AllocHGlobal(stride * (int)height);

            pitches = (uint)stride;
            lines = height;

            var w = (int)width;
            var h = (int)height;
            _dispatcher.BeginInvoke(() =>
            {
                // 位图在 UI 线程创建与写入,不能 Freeze
                _videoBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                VideoSizeChanged?.Invoke();
            });
        }

        return 1; // RV32 打包格式只有 1 个平面(之前错返回 height 导致 VLC 反复重协商)
    }

    private void VideoCleanupCallback(ref IntPtr opaque)
    {
        lock (_videoLock)
        {
            if (_videoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_videoBuffer);
                _videoBuffer = IntPtr.Zero;
            }
            _pendingFrame = null;
            _framePending = false;
        }
        _dispatcher.BeginInvoke(() =>
        {
            _videoBitmap = null;
            VideoSizeChanged?.Invoke();
        });
    }

    private IntPtr VideoLockCallback(IntPtr opaque, IntPtr planes)
    {
        lock (_videoLock)
        {
            if (_videoBuffer != IntPtr.Zero)
                Marshal.WriteIntPtr(planes, _videoBuffer);
        }
        return IntPtr.Zero;
    }

    private void VideoUnlockCallback(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
    }

    /// <summary>诊断:display 回调计数(测试观察用)。</summary>
    public int DisplayCallbackCount { get; private set; }

    /// <summary>诊断:format 回调计数。</summary>
    public int FormatCallbackCount { get; private set; }

    private void VideoDisplayCallback(IntPtr opaque, IntPtr picture)
    {
        DisplayCallbackCount++;

        var bitmap = _videoBitmap;
        byte[] frame;

        lock (_videoLock)
        {
            if (bitmap is null || _videoBuffer == IntPtr.Zero || _pendingFrame is null)
                return;

            // UI 线程还没消费上一帧 → 丢弃当前帧(避免堆积与撕裂)
            if (_framePending)
                return;

            Marshal.Copy(_videoBuffer, _pendingFrame, 0, _pendingFrame.Length);
            frame = _pendingFrame;
            _framePending = true;
        }

        var width = _frameStride / 4;
        var height = _frameHeight;
        var stride = _frameStride;

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                lock (_videoLock)
                {
                    if (_videoBitmap == bitmap)
                        bitmap.WritePixels(new Int32Rect(0, 0, width, height), frame, stride, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "视频帧写入失败");
            }
            finally
            {
                lock (_videoLock)
                {
                    _framePending = false;
                }
            }
        });
    }

    // ---------- VLC 播放事件(VLC 线程) ----------

    private void OnEndReached(object? sender, EventArgs e)
    {
        // 不在 VLC 线程调用 VLC API;前进策略交给 UI 层
        try
        {
            MediaEnded?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MediaEnded 事件处理失败");
        }
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        var path = CurrentPath ?? "(未知媒体)";
        Log.Error("播放错误: {Path}", path);
        PlaybackError?.Invoke(path, "无法播放该文件,格式可能不受支持。");
    }

    public void Dispose()
    {
        if (_playerLazy.IsValueCreated)
        {
            Player.EncounteredError -= OnEncounteredError;
            Player.EndReached -= OnEndReached;
            Player.Dispose();
        }
        if (_libVlcLazy.IsValueCreated)
            LibVLC.Dispose();

        lock (_videoLock)
        {
            if (_videoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_videoBuffer);
                _videoBuffer = IntPtr.Zero;
            }
        }
    }
}
