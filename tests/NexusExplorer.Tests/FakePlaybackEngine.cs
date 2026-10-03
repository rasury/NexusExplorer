using NexusExplorer.ApplicationLayer;
using NexusExplorer.Services;
namespace NexusExplorer.Tests;
internal sealed class FakePlaybackEngine : IPlaybackEngine
{
    public bool HoldFile { get; set; }
    private System.IO.FileStream? _handle;
    public event Action? MediaEnded;
    public event Action<string, string>? PlaybackError;
    public string? Path { get; private set; }
    public List<string> Played { get; } = new();
    public PlaybackSnapshot Snapshot { get; private set; } = new(false, TimeSpan.Zero, TimeSpan.Zero);
    public Task PlayAsync(string path, bool audio, CancellationToken cancellationToken = default)
    { Path = path; Played.Add(path); if (HoldFile) _handle = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read); Snapshot = new(true, TimeSpan.Zero, TimeSpan.FromSeconds(10)); return Task.CompletedTask; }
    public Task StopAndReleaseAsync() { _handle?.Dispose(); _handle = null; Path = null; Snapshot = new(false, TimeSpan.Zero, TimeSpan.Zero); return Task.CompletedTask; }
    public Task TogglePauseAsync() { Snapshot = Snapshot with { IsPlaying = !Snapshot.IsPlaying, IsPaused = Snapshot.IsPlaying }; return Task.CompletedTask; }
    public Task SeekAsync(float fraction) { Snapshot = Snapshot with { Position = Snapshot.Duration * fraction }; return Task.CompletedTask; }
    public Task SetVolumeAsync(int volume) => Task.CompletedTask;
    public void End() => MediaEnded?.Invoke();
    public void Error() => PlaybackError?.Invoke(Path ?? "", "test error");
    public void Dispose() { _handle?.Dispose(); }
}
