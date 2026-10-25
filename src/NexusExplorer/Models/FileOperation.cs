namespace NexusExplorer.Models;
public sealed class FileOperation
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Move";
    public int? FileId { get; set; }
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Backup { get; set; }
    public string State { get; set; } = "Prepared";
    public string? Error { get; set; }
    public string? Payload { get; set; }
    public string? Digest { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
