using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class BackgroundLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff"
    };

    private static readonly string[] DefaultCategories =
    {
        "Studio", "Garage", "Industrial", "Racing", "Patriotic", "Beach",
        "Downtown", "Mountains", "Desert", "Luxury", "Carbon Fiber",
        "Abstract", "Seasonal", "Custom"
    };

    private readonly BackgroundRepository _repository = new();

    public string LibraryRoot { get; }
    public string MasterFolder { get; }
    public string ThumbnailFolder { get; }

    public BackgroundLibraryService()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        LibraryRoot = Path.Combine(pictures, "Photomotive Studio Master", "Background Library");
        MasterFolder = Path.Combine(LibraryRoot, "Fullsize");
        ThumbnailFolder = Path.Combine(LibraryRoot, "Thumbnails");
        Directory.CreateDirectory(MasterFolder);
        Directory.CreateDirectory(ThumbnailFolder);
    }

    public IReadOnlyList<BackgroundRecord> GetAll() => _repository.GetAll();

    public IReadOnlyList<BackgroundRecord> Filter(string? search, string? category)
    {
        IEnumerable<BackgroundRecord> query = _repository.GetAll();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Tags.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (category.Equals("Favorites", StringComparison.OrdinalIgnoreCase))
                query = query.Where(x => x.IsFavorite);
            else if (category.Equals("Recent", StringComparison.OrdinalIgnoreCase))
                query = query.Where(x => x.LastUsedAt is not null).OrderByDescending(x => x.LastUsedAt);
            else
                query = query.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    public IReadOnlyList<string> GetCategories()
    {
        var categories = DefaultCategories
            .Concat(_repository.GetAll().Select(x => x.Category))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var result = new List<string> { "All", "Favorites", "Recent" };
        result.AddRange(categories);
        return result;
    }

    public async Task<BackgroundImportResult> ImportAsync(IEnumerable<string> sourceFiles, string category)
    {
        var result = new BackgroundImportResult();
        foreach (var source in sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(source) || !SupportedExtensions.Contains(Path.GetExtension(source)))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                var token = Guid.NewGuid().ToString("N");
                var extension = Path.GetExtension(source).ToLowerInvariant();
                var masterPath = Path.Combine(MasterFolder, token + extension);
                var thumbnailPath = Path.Combine(ThumbnailFolder, token + ".png");

                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                await using (var output = new FileStream(masterPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                {
                    await input.CopyToAsync(output);
                }

                var dimensions = CreateThumbnail(masterPath, thumbnailPath);
                var info = new FileInfo(masterPath);
                var record = new BackgroundRecord
                {
                    Name = Path.GetFileNameWithoutExtension(source),
                    Category = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim(),
                    FilePath = masterPath,
                    ThumbnailPath = thumbnailPath,
                    FileSize = info.Length,
                    CreatedAt = DateTime.Now,
                    PixelWidth = dimensions.Width,
                    PixelHeight = dimensions.Height
                };
                record.Id = _repository.Add(record);
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{Path.GetFileName(source)}: {ex.Message}");
            }
        }

        return result;
    }

    public void SaveMetadata(BackgroundRecord record) => _repository.Update(record);

    public void ToggleFavorite(BackgroundRecord record)
    {
        record.IsFavorite = !record.IsFavorite;
        _repository.Update(record);
    }

    public void MarkUsed(BackgroundRecord record)
    {
        record.LastUsedAt = DateTime.Now;
        record.UseCount++;
        _repository.Update(record);
    }

    public void Delete(BackgroundRecord record)
    {
        _repository.Delete(record.Id);
        TryDelete(record.ThumbnailPath);
        TryDelete(record.FilePath);
    }

    public (int Count, int Favorites, long TotalBytes) GetStatistics()
    {
        var all = _repository.GetAll();
        return (all.Count, all.Count(x => x.IsFavorite), all.Sum(x => x.FileSize));
    }

    public void RefreshImageMetadata(BackgroundRecord record)
    {
        if (!File.Exists(record.FilePath))
            return;

        try
        {
            using var stream = new FileStream(record.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            record.PixelWidth = frame.PixelWidth;
            record.PixelHeight = frame.PixelHeight;
            record.FileSize = new FileInfo(record.FilePath).Length;
            _repository.Update(record);
        }
        catch
        {
            // Leave existing metadata intact if the file is temporarily unavailable.
        }
    }

    private static (int Width, int Height) CreateThumbnail(string sourcePath, string thumbnailPath)
    {
        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        var maxDimension = 420.0;
        var scale = Math.Min(1.0, maxDimension / Math.Max(source.PixelWidth, source.PixelHeight));

        BitmapSource transformed = scale < 1.0
            ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
            : source;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(transformed));
        using var output = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(output);
        return (source.PixelWidth, source.PixelHeight);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Database removal succeeds even if a file is temporarily locked.
        }
    }
}

public sealed class BackgroundImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = new();
}
