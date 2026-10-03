namespace NexusExplorer.ApplicationLayer;

public enum PlayMode { Sequential, RepeatAll, RepeatOne, Shuffle }
public record PlaybackSnapshot(bool IsPlaying, TimeSpan Position, TimeSpan Duration, bool IsPaused = false);

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
