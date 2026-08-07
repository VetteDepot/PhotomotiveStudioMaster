namespace PhotomotiveStudioMaster.App.Models;

public sealed class BackgroundRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string FilePath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string StorageDisplay => FileSize < 1024 * 1024
        ? $"{FileSize / 1024.0:0} KB"
        : $"{FileSize / 1024.0 / 1024.0:0.0} MB";
}
