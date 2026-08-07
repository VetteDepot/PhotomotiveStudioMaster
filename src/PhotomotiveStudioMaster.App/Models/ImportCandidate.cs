namespace PhotomotiveStudioMaster.App.Models;

public sealed class ImportCandidate
{
    public string SourcePath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(SourcePath);
    public long SizeBytes { get; init; }
    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:0.0} KB"
        : $"{SizeBytes / (1024.0 * 1024.0):0.0} MB";
}
