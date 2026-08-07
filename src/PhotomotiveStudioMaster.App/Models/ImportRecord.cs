namespace PhotomotiveStudioMaster.App.Models;

public sealed class ImportRecord
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ImportedAt { get; set; }
    public string Status { get; set; } = "Imported";
    public string ExtractionPath { get; set; } = string.Empty;
}
