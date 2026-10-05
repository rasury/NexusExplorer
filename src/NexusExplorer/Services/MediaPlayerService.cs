using System.IO;
using System.Diagnostics;
using NexusExplorer.ApplicationLayer;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Serilog;
using NativePlayer = LibVLCSharp.Shared.MediaPlayer;

namespace NexusExplorer.Services;

/// <summary>One serialized native player; callbacks never synchronously wait for WPF.</summary>
public sealed class MediaPlayerService : IPlaybackEngine
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Dispatcher _dispatcher;
    private LibVLC? _vlc;
    private NativePlayer? _player;
    private long _generation;
    private long _activeGeneration;
    private bool _disposed;
    private int _volume = 100;
    private PlaybackTiming? _playbackTiming;
    private sealed class PlaybackTiming(long generation, long startedAt, string path)
    {
        public long Generation { get; } = generation;
        public long StartedAt { get; } = startedAt;
        public string Path { get; } = path;
        public int PlayingLogged;
        public int TimeLogged;
        public bool IsAac;
        public CancellationTokenSource? DurationCancellation;
        public Task? DurationTask;
        public long ExactDurationTicks = -1;
        public int DurationPending;
    }
    public bool HardwareDecoding { get; set; } = true;
    public NativePlayer? NativePlayer => _player;
    public int SelectedAudioTrack => _player?.AudioTrack ?? -1;
    public bool IsVlcReady => _player is not null;
    public string? VlcVersion => _vlc?.Version;
    public string? CurrentPath { get; private set; }
    public event Action? PlayerReady;
    public event Action? MediaEnded;
    public event Action<string, string>? PlaybackError;
    public PlaybackSnapshot Snapshot
    {
        get
        {
            var p = _player;
            if (p is null || _disposed) return new(false, TimeSpan.Zero, TimeSpan.Zero);
            var timing = ActiveTiming();
            var pending = timing is { IsAac: true } && Volatile.Read(ref timing.DurationPending) != 0;
            var exact = timing is null ? -1 : Interlocked.Read(ref timing.ExactDurationTicks);
            var duration = pending ? TimeSpan.Zero : exact >= 0 ? TimeSpan.FromTicks(exact) : TimeSpan.FromMilliseconds(Math.Max(0, p.Length));
            return new(p.IsPlaying, TimeSpan.FromMilliseconds(Math.Max(0, p.Time)), duration, p.State == VLCState.Paused,
                pending, timing is { IsAac: true } && !pending && exact < 0);
        }
    }
    public MediaPlayerService(Dispatcher? dispatcher = null) =>
        _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    public async Task InitializeAsync()
    {
        await _commands.WaitAsync();
        try { await Task.Run(EnsurePlayer); }
        finally { _commands.Release(); }
    }
    private void EnsurePlayer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_player is not null) return;
        var timing = Stopwatch.StartNew();
        Log.Information("VLC 初始化开始;封装版本 {WrapperVersion}", typeof(LibVLC).Assembly.GetName().Version);
        // The user reproduced the restore blip in official VLC with DirectSound
        // and requested WASAPI. In VLC 3 WASAPI is a stream backend of MMDevice.
        _vlc = new LibVLC(true, "--no-osd", "--aout=mmdevice", "--mmdevice-backend=wasapi", "--audio-resampler=speex_resampler");
        Log.Information("VLC 实例创建结束;耗时 {ElapsedMs:F1} ms", timing.Elapsed.TotalMilliseconds);
        _vlc.Log += (_, e) =>
        {
            if (e.Message.Contains("using audio output module", StringComparison.Ordinal)
                || e.Message.Contains("using aout stream module", StringComparison.Ordinal)
                || e.Message.Contains("using audio resampler module", StringComparison.Ordinal)
                || e.Message.Contains("using demux module", StringComparison.Ordinal)
                || e.Message.Contains("using audio decoder module", StringComparison.Ordinal))
                Log.Information("VLC 实际音频模块;请求 {Request};{Module}: {Message}", Interlocked.Read(ref _activeGeneration), e.Module, e.Message);
            else Log.Debug("VLC {Module}: {Message}", e.Module, e.Message);
        };
        var playerTiming = Stopwatch.StartNew();
        _player = new NativePlayer(_vlc) { EnableHardwareDecoding = HardwareDecoding };
        Log.Information("VLC 播放器创建结束;耗时 {ElapsedMs:F1} ms", playerTiming.Elapsed.TotalMilliseconds);
        _player.EndReached += OnEnded;
        _player.EncounteredError += OnError;
        _player.Playing += OnPlaying;
        _player.TimeChanged += OnTimeChanged;
        Log.Information("VLC 初始化结束;原生版本 {NativeVersion};耗时 {ElapsedMs:F1} ms", _vlc.Version, timing.Elapsed.TotalMilliseconds);
        try
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
                if (module.ModuleName.Equals("libvlc.dll", StringComparison.OrdinalIgnoreCase)
                    || module.ModuleName.Equals("libvlccore.dll", StringComparison.OrdinalIgnoreCase))
                    Log.Information("VLC 实际原生库 {NativePath}", module.FileName);
        }
        catch (Exception ex) { Log.Debug(ex, "读取已加载 VLC 库路径失败"); }
        Post(() => PlayerReady?.Invoke());
    }
    private void Post(Action action)
    {
        if (!_dispatcher.HasShutdownStarted)
            _ = _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }
    public async Task PlayAsync(string path, bool audio, CancellationToken cancellationToken = default)
    {
        var timing = Stopwatch.StartNew();
        var startedAt = Stopwatch.GetTimestamp();
        var generation = Interlocked.Increment(ref _generation);
        Log.Information("VLC 播放请求开始;请求 {Request};路径 {Path};播放器已初始化 {Ready}", generation, path, IsVlcReady);
        await _commands.WaitAsync(cancellationToken);
        try
        {
            Log.Information("VLC 播放命令就绪;请求 {Request};排队 {ElapsedMs:F1} ms", generation, timing.Elapsed.TotalMilliseconds);
            if (generation != Interlocked.Read(ref _generation)) return;
            await Task.Run(async () =>
            {
                var step = Stopwatch.StartNew();
                EnsurePlayer();
                Log.Information("VLC 播放初始化检查结束;请求 {Request};耗时 {ElapsedMs:F1} ms", generation, step.Elapsed.TotalMilliseconds);
                step.Restart();
                Interlocked.Exchange(ref _activeGeneration, 0);
                _player!.Stop(); _player.Media = null;
                await ReleaseDurationAnalysisAsync();
                Log.Information("VLC 清理旧媒体结束;请求 {Request};耗时 {ElapsedMs:F1} ms", generation, step.Elapsed.TotalMilliseconds);
                if (generation != Interlocked.Read(ref _generation) || cancellationToken.IsCancellationRequested) return;
                step.Restart();
                if (!File.Exists(path)) throw new FileNotFoundException("媒体文件不存在。", path);
                CurrentPath = path;
                using var media = new Media(_vlc!, new Uri(path));
                var isAac = Path.GetExtension(path).Equals(".aac", StringComparison.OrdinalIgnoreCase);
                // VLC 3's MP4 demuxer misreads seek indexes/timestamps in some HLS-derived
                // local MP4s. Keep native decoding/output, using libavformat for these containers.
                if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".m4v", StringComparison.OrdinalIgnoreCase) || isAac)
                    media.AddOption(":demux=avformat");
                _player.EnableHardwareDecoding = HardwareDecoding;
                _player.SetRole(audio ? MediaPlayerRole.Music : MediaPlayerRole.Video);
                _player.Volume = _volume; _player.SetRate(1);
                var playback = new PlaybackTiming(generation, startedAt, path) { IsAac = isAac };
                Volatile.Write(ref _playbackTiming, playback);
                Interlocked.Exchange(ref _activeGeneration, generation);
                if (isAac) StartDurationAnalysis(playback, cancellationToken);
                if (!_player.Play(media)) throw new OperationException("播放器拒绝打开该媒体。");
                Log.Information("VLC 打开调用返回;请求 {Request};打开步骤 {StepMs:F1} ms;请求累计 {ElapsedMs:F1} ms", generation, step.Elapsed.TotalMilliseconds, timing.Elapsed.TotalMilliseconds);
                Log.Information("播放 {Path};硬件解码 {Hardware};音量 {Volume};角色 {Role};请求音频输出 MMDevice/WASAPI;请求重采样 Speex", path, HardwareDecoding, _volume, audio ? "Music" : "Video");
            }, cancellationToken);
        }
        finally { _commands.Release(); }
    }
    public async Task StopAndReleaseAsync()
    {
        var timing = Stopwatch.StartNew();
        Interlocked.Increment(ref _generation);
        await _commands.WaitAsync();
        try
        {
            await Task.Run(async () =>
            {
                Interlocked.Exchange(ref _activeGeneration, 0);
                if (_player is not null && !_disposed) { _player.Stop(); _player.Media = null; }
                await ReleaseDurationAnalysisAsync();
                CurrentPath = null;
            });
            Log.Information("VLC 停止释放结束;耗时 {ElapsedMs:F1} ms", timing.Elapsed.TotalMilliseconds);
        }
        finally { _commands.Release(); }
    }
    public Task TogglePauseAsync() => CommandAsync(p => p.Pause());
    public Task SetAudioTrackAsync(int id) => CommandAsync(async p =>
    {
        var timing = Stopwatch.StartNew();
        var previous = SelectedAudioTrack;
        if (!p.AudioTrackDescription.Any(t => t.Id == id))
            throw new OperationException("所选音轨不可用，请重新打开音轨菜单。");
        if (previous == id) return;
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSelected(object? sender, MediaPlayerESSelectedEventArgs args)
        {
            if (args.Type == TrackType.Audio && args.Id == id) selected.TrySetResult();
        }
        p.ESSelected += OnSelected;
        try
        {
            // Preserve real VLC track selection: -1 destroys the decoder. No mute,
            // volume gate, artificial delay, seek or playback restart substitutes it.
            if (!p.SetAudioTrack(id)) throw new OperationException("播放器未能切换音轨。");
            if (p.AudioTrack != id) await selected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (p.AudioTrack != id) throw new TimeoutException();
        }
        catch (TimeoutException) { throw new OperationException("播放器未能切换音轨。"); }
        finally { p.ESSelected -= OnSelected; }
        Log.Information("底层音轨切换 {Previous} -> {Selected};实际音轨 {NativeTrack};静音 {Muted};媒体时间 {MediaTimeMs} ms;确认耗时 {ElapsedMs:F1} ms",
            previous, id, p.AudioTrack, p.Mute, p.Time, timing.Elapsed.TotalMilliseconds);
    });
    public Task SeekAsync(float fraction) => CommandAsync(p =>
    {
        var state = Snapshot;
        if (!state.IsDurationPending && state.Duration > TimeSpan.Zero)
            p.SeekTo(state.Duration * Math.Clamp(fraction, 0, 1));
    });

    private void StartDurationAnalysis(PlaybackTiming timing, CancellationToken cancellationToken)
    {
        timing.DurationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = timing.DurationCancellation.Token;
        Volatile.Write(ref timing.DurationPending, 1);
        timing.DurationTask = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var result = AdtsDurationReader.TryRead(timing.Path, token);
                token.ThrowIfCancellationRequested();
                if (result is not null)
                {
                    Interlocked.Exchange(ref timing.ExactDurationTicks, result.Duration.Ticks);
                    Log.Information("AAC 固定时长;请求 {Request};帧数 {Frames};采样率 {SampleRate};时长 {DurationMs:F3} ms;扫描 {ElapsedMs:F1} ms", timing.Generation, result.Frames, result.SampleRate, result.Duration.TotalMilliseconds, watch.Elapsed.TotalMilliseconds);
                }
                else Log.Warning("AAC 无法取得完整 ADTS 帧时长，将标注估算值;请求 {Request};路径 {Path}", timing.Generation, timing.Path);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "AAC 时长读取失败，将标注估算值;请求 {Request}", timing.Generation); }
            finally { Volatile.Write(ref timing.DurationPending, 0); }
        });
    }

    private async Task ReleaseDurationAnalysisAsync()
    {
        var timing = Interlocked.Exchange(ref _playbackTiming, null);
        if (timing?.DurationCancellation is null) return;
        timing.DurationCancellation.Cancel();
        if (timing.DurationTask is not null) await timing.DurationTask;
        timing.DurationCancellation.Dispose();
    }
    public Task SetVolumeAsync(int volume)
    { _volume = Math.Clamp(volume, 0, 100); return CommandAsync(p => p.Volume = _volume); }
    private Task CommandAsync(Action<NativePlayer> command) => CommandAsync(player =>
    {
        command(player);
        return Task.CompletedTask;
    });
    private async Task CommandAsync(Func<NativePlayer, Task> command)
    {
        var generation = Interlocked.Read(ref _generation);
        await _commands.WaitAsync();
        try
        {
            await Task.Run(async () => { if (!_disposed && _player is not null && generation == Interlocked.Read(ref _generation)) await command(_player); });
        }
        finally { _commands.Release(); }
    }
    private void OnEnded(object? sender, EventArgs args)
    {
        var generation = Interlocked.Read(ref _activeGeneration);
        Post(() => { if (generation != 0 && generation == Interlocked.Read(ref _generation)) MediaEnded?.Invoke(); });
    }
    private void OnError(object? sender, EventArgs args)
    {
        var generation = Interlocked.Read(ref _activeGeneration); var path = CurrentPath ?? "";
        Post(() => { if (generation != 0 && generation == Interlocked.Read(ref _generation)) PlaybackError?.Invoke(path, "无法播放该媒体，请查看日志并尝试关闭硬件解码。"); });
    }
    private PlaybackTiming? ActiveTiming()
    {
        var timing = Volatile.Read(ref _playbackTiming);
        return timing is not null && timing.Generation == Interlocked.Read(ref _activeGeneration)
            && timing.Generation == Interlocked.Read(ref _generation) ? timing : null;
    }
    private void OnPlaying(object? sender, EventArgs args)
    {
        if (ActiveTiming() is { } timing && Interlocked.Exchange(ref timing.PlayingLogged, 1) == 0)
            Log.Information("VLC Playing 事件;请求 {Request};路径 {Path};请求后 {ElapsedMs:F1} ms", timing.Generation, timing.Path, Stopwatch.GetElapsedTime(timing.StartedAt).TotalMilliseconds);
    }
    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args)
    {
        if (args.Time > 0 && ActiveTiming() is { } timing && Interlocked.Exchange(ref timing.TimeLogged, 1) == 0)
            Log.Information("VLC 首次播放时钟推进;请求 {Request};媒体时间 {MediaTimeMs} ms;请求后 {ElapsedMs:F1} ms", timing.Generation, args.Time, Stopwatch.GetElapsedTime(timing.StartedAt).TotalMilliseconds);
    }
    public void LogAudioDiagnostics()
    {
        if (_player is null) return;
        Log.Information("音轨 {Track};原生音轨 {NativeTrack};静音 {Muted};音轨描述 {Descriptions}",
            SelectedAudioTrack, _player.AudioTrack, _player.Mute, string.Join(", ", _player.AudioTrackDescription.Select(t => $"{t.Id}:{t.Name}")));
        using var media = _player.Media;
        if (media is not null)
            foreach (var track in media.Tracks.Where(t => t.TrackType == TrackType.Audio))
                Log.Information("音频轨 {Id};声道 {Channels};采样率 {Rate};码率 {Bitrate};设备 {Device}",
                    track.Id, track.Data.Audio.Channels, track.Data.Audio.Rate, track.Bitrate, _player.OutputDevice);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _commands.Wait();
        try
        {
            if (_disposed) return; _disposed = true; Interlocked.Increment(ref _generation);
            if (_player is not null) { _player.EndReached -= OnEnded; _player.EncounteredError -= OnError; _player.Playing -= OnPlaying; _player.TimeChanged -= OnTimeChanged; _player.Stop(); _player.Media = null; _player.Dispose(); }
            ReleaseDurationAnalysisAsync().GetAwaiter().GetResult();
            _vlc?.Dispose(); _player = null; _vlc = null;
        }
        finally { _commands.Release(); }
    }
}
