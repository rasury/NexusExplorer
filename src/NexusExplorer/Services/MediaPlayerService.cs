using System.IO;
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
    public bool HardwareDecoding { get; set; } = true;
    public NativePlayer? NativePlayer => _player;
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
            return new(p.IsPlaying, TimeSpan.FromMilliseconds(Math.Max(0, p.Time)), TimeSpan.FromMilliseconds(Math.Max(0, p.Length)), p.State == VLCState.Paused);
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
        // The user confirmed DirectSound removes the distortion heard with
        // automatic Windows output. Apply it to both audio and video playback.
        _vlc = new LibVLC(true, "--no-osd", "--aout=directsound");
        _vlc.Log += (_, e) => Log.Debug("VLC {Module}: {Message}", e.Module, e.Message);
        _player = new NativePlayer(_vlc) { EnableHardwareDecoding = HardwareDecoding };
        _player.EndReached += OnEnded;
        _player.EncounteredError += OnError;
        Post(() => PlayerReady?.Invoke());
    }
    private void Post(Action action)
    {
        if (!_dispatcher.HasShutdownStarted)
            _ = _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }
    public async Task PlayAsync(string path, bool audio, CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref _generation);
        await _commands.WaitAsync(cancellationToken);
        try
        {
            if (generation != Interlocked.Read(ref _generation)) return;
            await Task.Run(() =>
            {
                EnsurePlayer();
                Interlocked.Exchange(ref _activeGeneration, 0);
                _player!.Stop(); _player.Media = null;
                if (generation != Interlocked.Read(ref _generation) || cancellationToken.IsCancellationRequested) return;
                if (!File.Exists(path)) throw new FileNotFoundException("媒体文件不存在。", path);
                CurrentPath = path;
                using var media = new Media(_vlc!, new Uri(path));
                // VLC 3's MP4 demuxer misreads seek indexes/timestamps in some HLS-derived
                // local MP4s. Keep native decoding/output, using libavformat for these containers.
                if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".m4v", StringComparison.OrdinalIgnoreCase))
                    media.AddOption(":demux=avformat");
                _player.EnableHardwareDecoding = HardwareDecoding;
                _player.SetRole(audio ? MediaPlayerRole.Music : MediaPlayerRole.Video);
                _player.Volume = _volume; _player.SetRate(1);
                Interlocked.Exchange(ref _activeGeneration, generation);
                if (!_player.Play(media)) throw new OperationException("播放器拒绝打开该媒体。");
                Log.Information("播放 {Path};硬件解码 {Hardware};音量 {Volume};角色 {Role};请求音频输出 DirectSound", path, HardwareDecoding, _volume, audio ? "Music" : "Video");
            }, cancellationToken);
        }
        finally { _commands.Release(); }
    }
    public async Task StopAndReleaseAsync()
    {
        Interlocked.Increment(ref _generation);
        await _commands.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                Interlocked.Exchange(ref _activeGeneration, 0);
                if (_player is not null && !_disposed) { _player.Stop(); _player.Media = null; }
                CurrentPath = null;
            });
        }
        finally { _commands.Release(); }
    }
    public Task TogglePauseAsync() => CommandAsync(p => p.Pause());
    public Task SetAudioTrackAsync(int id) => CommandAsync(p => p.SetAudioTrack(id));
    public Task SeekAsync(float fraction) => CommandAsync(p =>
    {
        if (p.Length > 0) p.SeekTo(TimeSpan.FromMilliseconds(p.Length * Math.Clamp(fraction, 0, 1)));
    });
    public Task SetVolumeAsync(int volume)
    { _volume = Math.Clamp(volume, 0, 100); return CommandAsync(p => p.Volume = _volume); }
    private async Task CommandAsync(Action<NativePlayer> command)
    {
        var generation = Interlocked.Read(ref _generation);
        await _commands.WaitAsync();
        try
        {
            await Task.Run(() => { if (!_disposed && _player is not null && generation == Interlocked.Read(ref _generation)) command(_player); });
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
    public void LogAudioDiagnostics()
    {
        if (_player is null) return;
        Log.Information("音轨 {Track};音轨描述 {Descriptions}",
            _player.AudioTrack, string.Join(", ", _player.AudioTrackDescription.Select(t => $"{t.Id}:{t.Name}")));
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
            if (_player is not null) { _player.EndReached -= OnEnded; _player.EncounteredError -= OnError; _player.Stop(); _player.Media = null; _player.Dispose(); }
            _vlc?.Dispose(); _player = null; _vlc = null;
        }
        finally { _commands.Release(); }
    }
}
