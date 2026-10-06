namespace NexusExplorer.ApplicationLayer;

public enum PlayMode { Sequential, RepeatAll, RepeatOne, Shuffle }
public record PlaybackSnapshot(bool IsPlaying, TimeSpan Position, TimeSpan Duration, bool IsPaused = false,
    bool IsDurationPending = false, bool IsDurationEstimated = false);

/// <summary>Application contract independent of LibVLC and WPF controls.</summary>
public interface IPlaybackEngine : IDisposable
{
    event Action? MediaEnded;
    event Action<string, string>? PlaybackError;
    Task PlayAsync(string path, bool audio, CancellationToken cancellationToken = default);
    Task StopAndReleaseAsync();
    Task TogglePauseAsync();
    Task SeekAsync(float fraction);
    Task SetVolumeAsync(int volume);
    PlaybackSnapshot Snapshot { get; }
}

public readonly record struct PlaybackToken(long EngineEpoch, long MediaGeneration);
public sealed record AudioTrackInfo(long Id, string Name, string? Language, string? Codec, bool IsSelected);
public interface IMediaPlaybackControls
{
    bool HardwareDecoding { get; set; }
    string? ActiveHardwareDecoder { get; }
    PlaybackToken CurrentToken { get; }
    IReadOnlyList<AudioTrackInfo> AudioTracks { get; }
    long SelectedAudioTrack { get; }
    Task SetAudioTrackAsync(long id, PlaybackToken token);
    event Action? VideoClicked;
}
public interface IPlaybackLifetime
{
    Task InitializeAsync();
    Task ShutdownAsync();
    string? EngineVersion { get; }
    void AttachSurface(IntPtr hwnd);
}
public interface IPlaybackDiagnostics
{
    Task RecordDiagnosticsAsync();
}
