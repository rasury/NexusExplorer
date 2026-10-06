using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Threading;
using NexusExplorer.ApplicationLayer;
using NexusExplorer.Services;
using Serilog;

namespace NexusExplorer.Infrastructure.Playback.Mpv;

/// <summary>One native core; event pump never waits for UI or command ordering locks.</summary>
public sealed class MpvPlaybackEngine : IPlaybackEngine, IMediaPlaybackControls, IPlaybackLifetime, IPlaybackDiagnostics
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly object _state = new();
    private readonly object _shutdownLock = new();
    private readonly object _volumeLock = new();
    private readonly MpvDiagnostics _diagnostics = new();
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource> _pending = new();
    private readonly ConcurrentDictionary<ulong, (long Generation, string Name)> _observed = new();
    private readonly TaskCompletionSource _shutdown = NewCompletion();
    private IntPtr _handle, _surface;
    private Thread? _pump;
    private Task? _shutdownTask;
    private Task? _volumeTask;
    private volatile bool _pumpStopped, _closing, _disposed;
    private long _generation, _requestId, _observeId;
    private int _volume = 100;
    private Session? _active;
    private PlaybackSnapshot _snapshot = new(false, TimeSpan.Zero, TimeSpan.Zero);
    private AudioTrackInfo[] _tracks = Array.Empty<AudioTrackInfo>();
    private string? _hardwareDecoder, _audioOutput, _videoOutput;
    private long _selected = -1;
    private bool _nativeIdle = true, _pause;
    private Exception? _pumpFailure;
    private sealed class Session(long generation, string path, long startedAt)
    {
        public long Generation { get; } = generation;
        public string Path { get; } = path;
        public long StartedAt { get; } = startedAt;
        public long Entry = -1;
        public bool Submitted, Loaded, Ended, ExplicitStop, TimeLogged;
        public double Position, Duration;
        public bool DurationPending, DurationEstimated;
        public TimeSpan? ExactDuration;
        public CancellationTokenSource? DurationCancellation;
        public Task? DurationTask;
        public TaskCompletionSource Load { get; } = NewCompletion();
        public TaskCompletionSource End { get; } = NewCompletion();
    }
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HardwareDecoding { get; set; } = true;
    public string? ActiveHardwareDecoder => Volatile.Read(ref _hardwareDecoder);
    public string? AudioOutput => Volatile.Read(ref _audioOutput);
    public string? VideoOutput => Volatile.Read(ref _videoOutput);
    public string? EngineVersion { get; private set; }
    public string? CurrentPath => Volatile.Read(ref _active)?.Path;
    public PlaybackToken CurrentToken => new(1, Volatile.Read(ref _active)?.Generation ?? 0);
    public IReadOnlyList<AudioTrackInfo> AudioTracks => Volatile.Read(ref _tracks);
    public long SelectedAudioTrack => Interlocked.Read(ref _selected);
    public PlaybackSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public Task RecordDiagnosticsAsync() => Task.Run(() => _diagnostics.Dump("用户 F6 标记"));
    internal async Task<string?> ReadNativeForVerificationAsync(string name)
    {
        await _commands.WaitAsync();
        try { EnsureUsable(); return _handle == IntPtr.Zero ? null : await Task.Run(() => MpvNative.GetString(_handle, name)); }
        finally { _commands.Release(); }
    }
    public event Action? MediaEnded;
    public event Action<string, string>? PlaybackError;
    public event Action? VideoClicked;
    public MpvPlaybackEngine(Dispatcher? dispatcher = null) => _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    public void AttachSurface(IntPtr hwnd) => _surface = hwnd;
    private void Post(Action action)
    {
        if (!_dispatcher.HasShutdownStarted)
            _ = _dispatcher.BeginInvoke(() => { if (!_closing && !_disposed) action(); });
    }
    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed || _closing, this);
        if (_pumpFailure is not null) throw new OperationException("播放器事件接收失败，已停止文件操作。", _pumpFailure);
    }
    public async Task InitializeAsync()
    {
        await _commands.WaitAsync();
        try { EnsureUsable(); await Task.Run(EnsureCore); }
        finally { _commands.Release(); }
    }
    private void EnsureCore()
    {
        if (_handle != IntPtr.Zero) return;
        var watch = Stopwatch.StartNew();
        var path = Path.Combine(AppContext.BaseDirectory, "native", "mpv", "win-x64", "libmpv-2.dll");
        string hash;
        using (var nativeFile = File.OpenRead(path)) hash = Convert.ToHexString(SHA256.HashData(nativeFile));
        if (!hash.Equals("704b75444726add17add8278a9cb5031ad20f01718989d8fe8dd34a1c2faab8c", StringComparison.OrdinalIgnoreCase))
            throw new OperationException("mpv 原生库校验失败，请重新安装完整程序包。");
        if (MpvNative.ApiVersion() != 0x20005) throw new OperationException("mpv Client API 版本与固定 SDK 不匹配。");
        var handle = MpvNative.Create();
        if (handle == IntPtr.Zero) throw new OperationException("mpv 核心创建失败。");
        try
        {
            foreach (var (name, value) in new[] {
                ("config", "no"), ("load-scripts", "no"), ("idle", "yes"), ("terminal", "no"),
                ("osc", "no"), ("osd-level", "0"), ("input-default-bindings", "no"), ("input-vo-keyboard", "no"),
                ("keep-open", "no"), ("resume-playback", "no"), ("audio-display", "no"),
                ("ao", "wasapi"), ("volume", "100"), ("speed", "1"),
                ("input-conf", Path.Combine(AppContext.BaseDirectory, "native", "mpv", "input.conf")) })
                MpvNative.Check(MpvNative.SetOption(handle, name, value), "初始化选项 " + name);
            if (_surface != IntPtr.Zero)
                MpvNative.Check(MpvNative.SetOption(handle, "wid", SurfaceId()), "视频窗口");
            MpvNative.Check(MpvNative.Initialize(handle), "初始化");
            _handle = handle;
            EngineVersion = MpvNative.GetString(handle, "mpv-version");
            if (EngineVersion != "mpv v0.41.0-1100-gc15296420") throw new OperationException("mpv 原生版本与已验收构建不匹配。");
            Log.Information("mpv 初始化完成;版本 {Version};API 2.5;原生库 {NativePath};SHA256 {Hash};FFmpeg {Ffmpeg};耗时 {ElapsedMs:F1} ms",
                EngineVersion, path, hash, MpvNative.GetString(handle, "ffmpeg-version"), watch.Elapsed.TotalMilliseconds);
            MpvNative.Check(MpvNative.RequestLogs(handle, "v"), "日志");
            Observe(0, "idle-active", MpvNative.Format.Flag);
            Observe(0, "pause", MpvNative.Format.Flag);
            _pump = new Thread(Pump) { IsBackground = true, Name = "Nexus mpv events" };
            _pump.Start();
        }
        catch
        {
            _handle = IntPtr.Zero;
            MpvNative.Destroy(handle);
            throw;
        }
    }
    private string SurfaceId() => unchecked((uint)_surface.ToInt64()).ToString(CultureInfo.InvariantCulture);
    private void Observe(long generation, string name, MpvNative.Format format)
    {
        var id = (ulong)Interlocked.Increment(ref _observeId);
        _observed[id] = (generation, name);
        MpvNative.Check(MpvNative.Observe(_handle, id, name, format), "观察 " + name);
    }
    private void ObserveMedia(long generation)
    {
        foreach (var (id, item) in _observed.ToArray())
            if (item.Generation != 0) { MpvNative.Check(MpvNative.Unobserve(_handle, id), "移除观察"); _observed.TryRemove(id, out _); }
        foreach (var name in new[] { "time-pos", "duration" }) Observe(generation, name, MpvNative.Format.Double);
        foreach (var name in new[] { "hwdec-current", "current-ao", "current-vo", "aid", "demuxer" }) Observe(generation, name, MpvNative.Format.String);
        foreach (var name in new[] { "track-list", "audio-params", "audio-out-params" }) Observe(generation, name, MpvNative.Format.Node);
    }
    private async Task SendAsync(params string[] values)
        => await SendAsync(null, values);
    private async Task SendAsync(Action? accepted, params string[] values)
    {
        if (_pumpFailure is not null) throw new OperationException("播放器事件接收失败。", _pumpFailure);
        var id = (ulong)Interlocked.Increment(ref _requestId);
        var completion = NewCompletion(); _pending[id] = completion;
        try
        {
            await Task.Run(() =>
            {
                using var arguments = new MpvNative.Arguments(values);
                MpvNative.Check(MpvNative.CommandAsync(_handle, id, arguments.Pointer), values[0]);
                accepted?.Invoke();
            });
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch { _pending.TryRemove(id, out _); throw; }
    }
    public async Task PlayAsync(string path, bool audio, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var generation = Interlocked.Increment(ref _generation);
        Log.Information("mpv 播放请求开始;会话 {Generation};路径 {Path};核心已初始化 {Ready}", generation, path, _handle != IntPtr.Zero);
        Volatile.Read(ref _active)?.Load.TrySetCanceled();
        await _commands.WaitAsync(cancellationToken);
        Session? session = null;
        try
        {
            EnsureUsable();
            if (generation != Interlocked.Read(ref _generation)) return;
            Log.Information("mpv 播放命令就绪;会话 {Generation};排队 {ElapsedMs:F1} ms", generation, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            await Task.Run(EnsureCore);
            await StopCoreAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref _generation)) return;
            if (!await Task.Run(() => File.Exists(path))) throw new FileNotFoundException("媒体文件不存在。", path);
            if (!audio && _surface == IntPtr.Zero) throw new OperationException("视频窗口尚未就绪。");
            if (_surface != IntPtr.Zero) await SendAsync("set", "wid", SurfaceId());
            await SendAsync("set", "hwdec", HardwareDecoding ? "auto-safe" : "no");
            await SendAsync("set", "vid", audio ? "no" : "auto");
            await SendAsync("set", "aid", "auto");
            await SendAsync("set", "pause", "no");
            await SendAsync("set", "volume", _volume.ToString(CultureInfo.InvariantCulture));
            await SendAsync("set", "speed", "1");
            session = new Session(generation, path, startedAt);
            lock (_state)
            {
                _active = session; _tracks = Array.Empty<AudioTrackInfo>(); _selected = -1;
                _hardwareDecoder = _audioOutput = _videoOutput = null;
                Publish();
            }
            await Task.Run(() => ObserveMedia(generation));
            if (Path.GetExtension(path).Equals(".aac", StringComparison.OrdinalIgnoreCase)) StartDuration(session, cancellationToken);
            Log.Information("mpv 播放请求;会话 {Generation};路径 {Path};硬解请求 {Hardware}", generation, path, HardwareDecoding);
            await SendAsync(() => session.Submitted = true, "loadfile", Path.GetFullPath(path), "replace");
            await session.Load.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref _generation)) throw new OperationCanceledException();
        }
        catch
        {
            if (session is not null && ReferenceEquals(session, Volatile.Read(ref _active))) await StopCoreAsync();
            throw;
        }
        finally { _commands.Release(); }
    }
    public async Task StopAndReleaseAsync()
    {
        var timing = Stopwatch.StartNew();
        Interlocked.Increment(ref _generation);
        Volatile.Read(ref _active)?.Load.TrySetCanceled();
        await _commands.WaitAsync();
        try { await StopCoreAsync(); Log.Information("mpv 停止屏障完成;含排队 {ElapsedMs:F1} ms", timing.Elapsed.TotalMilliseconds); }
        finally { _commands.Release(); }
    }
    private async Task StopCoreAsync()
    {
        var session = Volatile.Read(ref _active);
        var timing = Stopwatch.StartNew();
        if (session is not null) { session.ExplicitStop = true; session.DurationCancellation?.Cancel(); }
        if (_handle != IntPtr.Zero)
        {
            await SendAsync("stop");
            if (session is { Submitted: true, Ended: false }) await session.End.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // A fresh read after the stop reply, never a pre-load cached Idle flag.
            var idle = await Task.Run(() => MpvNative.GetString(_handle, "idle-active"));
            if (idle != "yes") throw new OperationException("播放器尚未释放当前媒体，已停止文件操作。");
        }
        if (session?.DurationTask is not null) await session.DurationTask;
        session?.DurationCancellation?.Dispose();
        lock (_state)
        {
            _active = null; _nativeIdle = true; _pause = false;
            _tracks = Array.Empty<AudioTrackInfo>(); _selected = -1;
            _hardwareDecoder = _audioOutput = _videoOutput = null;
            Publish();
        }
        if (session is not null) Log.Information("mpv 停止释放完成;会话 {Generation};耗时 {ElapsedMs:F1} ms", session.Generation, timing.Elapsed.TotalMilliseconds);
    }
    public Task TogglePauseAsync() => ControlAsync(async () =>
    {
        var paused = await Task.Run(() => MpvNative.GetString(_handle, "pause"));
        await SendAsync("set", "pause", paused == "yes" ? "no" : "yes");
    });
    public Task SetVolumeAsync(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        lock (_volumeLock) return _volumeTask is { IsCompleted: false } ? _volumeTask : _volumeTask = ApplyVolumeAsync();
    }
    private async Task ApplyVolumeAsync()
    {
        int applied;
        do
        {
            applied = Volatile.Read(ref _volume);
            await ControlAsync(async () => await SendAsync("set", "volume", applied.ToString(CultureInfo.InvariantCulture)), requireMedia: false);
        } while (applied != Volatile.Read(ref _volume));
    }
    public Task SeekAsync(float fraction) => ControlAsync(async () =>
    {
        if (!float.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
        var snapshot = Snapshot;
        if (snapshot.IsDurationPending || snapshot.Duration <= TimeSpan.Zero) return;
        var target = snapshot.Duration.TotalSeconds * Math.Clamp(fraction, 0, 1);
        Log.Information("mpv 跳转;会话 {Generation};原位置 {From:F3};目标 {To:F3} s", CurrentToken.MediaGeneration, snapshot.Position.TotalSeconds, target);
        await SendAsync("seek", target.ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
    });
    public Task SetAudioTrackAsync(long id, PlaybackToken token) => ControlAsync(async () =>
    {
        if (CurrentToken != token) throw new OperationException("媒体已切换，请重新打开音轨菜单。");
        if (id != -1 && !AudioTracks.Any(track => track.Id == id)) throw new OperationException("所选音轨不可用。");
        await SendAsync("set", "aid", id == -1 ? "no" : id.ToString(CultureInfo.InvariantCulture));
        var actual = await Task.Run(() => MpvNative.GetString(_handle, "aid"));
        if (CurrentToken != token || actual != (id == -1 ? "no" : id.ToString(CultureInfo.InvariantCulture))) throw new OperationException("播放器未能确认音轨切换。");
        Interlocked.Exchange(ref _selected, id);
        Log.Information("mpv 真实音轨切换;会话 {Generation};请求 {Track};实际 aid {Actual};静音未改动", token.MediaGeneration, id, actual);
    });
    private async Task ControlAsync(Func<Task> action, bool requireMedia = true)
    {
        var token = CurrentToken;
        await _commands.WaitAsync();
        try
        {
            EnsureUsable();
            if (_handle == IntPtr.Zero || (requireMedia && (token.MediaGeneration == 0 || token != CurrentToken || token.MediaGeneration != Interlocked.Read(ref _generation)))) return;
            await action();
        }
        finally { _commands.Release(); }
    }
    private void Pump()
    {
        try
        {
            while (!_pumpStopped)
            {
                var ev = Marshal.PtrToStructure<MpvNative.Event>(MpvNative.WaitEvent(_handle, .2));
                switch ((MpvNative.EventId)ev.Id)
                {
                    case MpvNative.EventId.None: break;
                    case MpvNative.EventId.Shutdown:
                        _shutdown.TrySetResult(); _pumpStopped = true;
                        if (!_closing) throw new OperationException("mpv 核心意外退出，请关闭并重新打开软件。");
                        break;
                    case MpvNative.EventId.SetPropertyReply:
                    case MpvNative.EventId.CommandReply:
                        if (_pending.TryRemove(ev.UserData, out var reply))
                        { if (ev.Error < 0) reply.TrySetException(new OperationException("mpv 命令失败：" + ev.Error)); else reply.TrySetResult(); }
                        break;
                    case MpvNative.EventId.LogMessage: NativeLog(Marshal.PtrToStructure<MpvNative.LogMessage>(ev.Data)); break;
                    case MpvNative.EventId.StartFile: StartFile(Marshal.ReadInt64(ev.Data)); break;
                    case MpvNative.EventId.EndFile: EndFile(Marshal.PtrToStructure<MpvNative.EndFile>(ev.Data)); break;
                    case MpvNative.EventId.FileLoaded: Loaded(); break;
                    case MpvNative.EventId.ClientMessage:
                        var message = Marshal.PtrToStructure<MpvNative.ClientMessage>(ev.Data);
                        if (message.Count > 0 && MpvNative.Text(Marshal.ReadIntPtr(message.Args)) == "nexus-video-click")
                        {
                            var token = CurrentToken;
                            Post(() => { if (token == CurrentToken && token.MediaGeneration == Interlocked.Read(ref _generation)) VideoClicked?.Invoke(); });
                        }
                        break;
                    case MpvNative.EventId.Seek: Log.Debug("mpv 跳转开始;会话 {Generation}", CurrentToken.MediaGeneration); break;
                    case MpvNative.EventId.PlaybackRestart: Log.Debug("mpv 播放恢复;会话 {Generation}", CurrentToken.MediaGeneration); break;
                    case MpvNative.EventId.QueueOverflow: throw new OperationException("mpv 事件队列溢出，播放状态不再可信。");
                    case MpvNative.EventId.Hook: throw new OperationException("mpv 收到未注册的 hook。");
                    case MpvNative.EventId.PropertyChange:
                        if (_observed.TryGetValue(ev.UserData, out var observer))
                        { var property = Marshal.PtrToStructure<MpvNative.Property>(ev.Data); PropertyChanged(observer.Generation, observer.Name, MpvNative.CopyProperty(property)); }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _pumpFailure = ex; Log.Error(ex, "mpv 事件接收失败");
            Volatile.Read(ref _active)?.Load.TrySetException(ex);
            Post(() => PlaybackError?.Invoke(CurrentPath ?? "", "播放器状态异常，请关闭并重新打开软件。"));
        }
        finally
        {
            foreach (var (id, pending) in _pending) if (_pending.TryRemove(id, out _)) pending.TrySetException(new OperationCanceledException("mpv 事件线程已退出。"));
        }
    }
    private void StartFile(long entry)
    {
        lock (_state) { if (_active is { } session) session.Entry = entry; }
    }
    private void Loaded()
    {
        lock (_state)
        {
            if (_active is not { } session || session.ExplicitStop || session.Ended) return;
            session.Loaded = true; _nativeIdle = false; Publish(); session.Load.TrySetResult();
            Log.Information("mpv 媒体加载完成;会话 {Generation};条目 {Entry};耗时 {ElapsedMs:F1} ms", session.Generation, session.Entry, Stopwatch.GetElapsedTime(session.StartedAt).TotalMilliseconds);
        }
    }
    private void EndFile(MpvNative.EndFile end)
    {
        Session? session;
        lock (_state)
        {
            session = _active;
            if (session is null || session.Entry != end.EntryId) return;
            session.Ended = true;
            if (end.Reason == 0 && !session.ExplicitStop && (session.ExactDuration?.TotalSeconds ?? session.Duration) > 0)
                session.Position = session.ExactDuration?.TotalSeconds ?? session.Duration;
            Publish(); session.End.TrySetResult();
            if (!session.Loaded) session.Load.TrySetException(new OperationException("无法打开该媒体，mpv 错误：" + end.Error));
        }
        Log.Information("mpv 媒体已卸载;会话 {Generation};条目 {Entry};原因 {Reason};错误 {Error}", session.Generation, end.EntryId, end.Reason, end.Error);
        if (end.Reason == 0 && !session.ExplicitStop)
            Post(() => { if (session.Generation == Interlocked.Read(ref _generation)) MediaEnded?.Invoke(); });
        else if (end.Reason == 4 && session.Loaded)
            Post(() => { if (session.Generation == Interlocked.Read(ref _generation)) PlaybackError?.Invoke(session.Path, "无法播放该媒体，请查看日志。"); });
    }
    private void PropertyChanged(long generation, string name, object? value)
    {
        lock (_state)
        {
            if (generation == 0)
            {
                if (name == "idle-active" && value is bool idle) _nativeIdle = idle;
                if (name == "pause" && value is bool pause) _pause = pause;
                Publish(); return;
            }
            if (_active is not { } session || session.Generation != generation || session.Ended) return;
            switch (name)
            {
                case "time-pos" when value is double time && double.IsFinite(time):
                    session.Position = Math.Max(0, time);
                    if (time > 0 && !session.TimeLogged)
                    { session.TimeLogged = true; Log.Information("mpv 首次时钟推进;会话 {Generation};位置 {Time:F3} s;请求后 {ElapsedMs:F1} ms", generation, time, Stopwatch.GetElapsedTime(session.StartedAt).TotalMilliseconds); }
                    break;
                case "duration" when value is double duration && double.IsFinite(duration): session.Duration = Math.Max(0, duration); break;
                case "hwdec-current": _hardwareDecoder = value as string; Log.Information("mpv 实际硬解;会话 {Generation};模块 {Module}", generation, _hardwareDecoder); break;
                case "current-ao": _audioOutput = value as string; Log.Information("mpv 实际音频输出;会话 {Generation};模块 {Module}", generation, _audioOutput); break;
                case "current-vo": _videoOutput = value as string; Log.Information("mpv 实际视频输出;会话 {Generation};模块 {Module}", generation, _videoOutput); break;
                case "aid": _selected = value is string track && long.TryParse(track, out var id) ? id : -1; break;
                case "track-list":
                    _tracks = value is List<object?> list ? list.OfType<Dictionary<string, object?>>()
                        .Where(track => track.GetValueOrDefault("type") as string == "audio" && track.GetValueOrDefault("id") is long)
                        .Select(track => new AudioTrackInfo((long)track["id"]!, track.GetValueOrDefault("title") as string ?? "音轨 " + track["id"],
                            track.GetValueOrDefault("lang") as string, track.GetValueOrDefault("codec") as string, track.GetValueOrDefault("selected") is true)).ToArray() : Array.Empty<AudioTrackInfo>();
                    if (_tracks.FirstOrDefault(track => track.IsSelected) is { } selected) _selected = selected.Id;
                    break;
                case "audio-params":
                case "audio-out-params": Log.Information("mpv 音频格式;会话 {Generation};类型 {Type};格式 {@Format}", generation, name, value); break;
                case "demuxer": Log.Information("mpv 解复用;会话 {Generation};模块 {Module}", generation, value); break;
            }
            Publish();
        }
    }
    private void Publish()
    {
        var session = _active;
        if (session is null) { Volatile.Write(ref _snapshot, new(false, TimeSpan.Zero, TimeSpan.Zero)); return; }
        var duration = session.DurationPending ? TimeSpan.Zero : session.ExactDuration ?? TimeSpan.FromSeconds(session.Duration);
        Volatile.Write(ref _snapshot, new(session.Loaded && !session.Ended && !_nativeIdle && !_pause,
            TimeSpan.FromSeconds(session.Position), duration, session.Loaded && !session.Ended && _pause, session.DurationPending, session.DurationEstimated && session.ExactDuration is null));
        _diagnostics.Sample(CurrentToken, _snapshot, _hardwareDecoder, _audioOutput, _videoOutput);
    }
    private void StartDuration(Session session, CancellationToken external)
    {
        session.DurationCancellation = CancellationTokenSource.CreateLinkedTokenSource(external);
        var token = session.DurationCancellation.Token;
        lock (_state) { session.DurationPending = true; session.DurationEstimated = true; Publish(); }
        session.DurationTask = Task.Run(() =>
        {
            try
            {
                var result = AdtsDurationReader.TryRead(session.Path, token);
                token.ThrowIfCancellationRequested();
                lock (_state)
                {
                    if (!ReferenceEquals(_active, session)) return;
                    session.ExactDuration = result?.Duration;
                    Log.Information("mpv AAC 时长;会话 {Generation};精确时长 {Duration}", session.Generation, result?.Duration);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "mpv AAC 时长扫描失败;会话 {Generation}", session.Generation); }
            finally { lock (_state) { session.DurationPending = false; if (ReferenceEquals(_active, session)) Publish(); } }
        });
    }
    private void NativeLog(MpvNative.LogMessage message)
    {
        var prefix = MpvNative.Text(message.Prefix); var text = MpvNative.Text(message.Text).TrimEnd();
        if (message.LogLevel <= 20) Log.Error("mpv {Module}: {Message}", prefix, text);
        else if (message.LogLevel <= 30) Log.Warning("mpv {Module}: {Message}", prefix, text);
        else if (text.Contains("Using hardware decoding") || text.Contains("Initializing GPU context") || text.Contains("Device Name:")) Log.Information("mpv 输出路径 {Module}: {Message}", prefix, text);
        else Log.Debug("mpv {Module}: {Message}", prefix, text);
        if (message.LogLevel <= 30) _diagnostics.Dump("原生 warning/error", throttle: true);
    }
    public Task ShutdownAsync()
    {
        lock (_shutdownLock) return _shutdownTask ??= ShutdownCoreAsync();
    }
    private async Task ShutdownCoreAsync()
    {
        _closing = true; Interlocked.Increment(ref _generation);
        Volatile.Read(ref _active)?.Load.TrySetCanceled();
        await _commands.WaitAsync();
        try
        {
            if (_handle == IntPtr.Zero) { _disposed = true; return; }
            var graceful = _pumpFailure is null;
            if (graceful)
            {
                try
                {
                    await StopCoreAsync();
                    // quit may shut down before its reply. SHUTDOWN is the terminal barrier.
                    await Task.Run(() =>
                    {
                        using var arguments = new MpvNative.Arguments("quit");
                        MpvNative.Check(MpvNative.CommandAsync(_handle, 0, arguments.Pointer), "退出");
                    });
                    await _shutdown.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                catch (Exception ex) { graceful = false; Log.Warning(ex, "mpv 正常退出未完成，将终止原生核心并等待其释放"); }
            }
            if (!graceful)
            {
                var session = Volatile.Read(ref _active);
                session?.DurationCancellation?.Cancel();
                if (session?.DurationTask is not null) await session.DurationTask;
                _pumpStopped = true; MpvNative.Wakeup(_handle);
            }
            await Task.Run(() => { if (_pump is not null && !_pump.Join(5000)) throw new OperationException("mpv 事件线程未退出。"); });
            await Task.Run(() => MpvNative.Destroy(_handle));
            _handle = IntPtr.Zero; _disposed = true;
            lock (_state) { _active?.DurationCancellation?.Dispose(); _active = null; _tracks = Array.Empty<AudioTrackInfo>(); Publish(); }
            Log.Information("mpv 核心与事件线程已退出");
        }
        finally { _commands.Release(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess())
        {
            // Normal window Closing awaits ShutdownAsync first. Never block its HWND thread here.
            _ = ShutdownAsync().ContinueWith(task => { if (task.Exception is not null) Log.Error(task.Exception, "mpv 兜底关闭失败"); }, TaskScheduler.Default);
        }
        else ShutdownAsync().GetAwaiter().GetResult();
    }
}
