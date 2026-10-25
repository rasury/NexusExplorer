namespace NexusExplorer.ApplicationLayer;

/// <summary>File IDs, never a cursor into a mutable UI collection.</summary>
public sealed class PlaybackSession
{
    public List<int> Queue { get; } = new();
    public int Index { get; private set; } = -1;
    public HashSet<int> Classified { get; } = new();
    public void Open(IEnumerable<int> ids, int currentId)
    { Queue.Clear(); Queue.AddRange(ids.Distinct()); Classified.Clear(); Index = Queue.IndexOf(currentId); }
    public int? Step(int offset, bool classification = false)
    {
        for (var next = Index + offset; next >= 0 && next < Queue.Count; next += offset)
            if (!classification || !Classified.Contains(Queue[next])) { Index = next; return Queue[next]; }
        return null;
    }
    public void SetCurrent(int id) => Index = Queue.IndexOf(id);
}
