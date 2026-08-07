using System.Security.Cryptography;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class ImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff",
        ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2"
    };

    private readonly ImportRepository _repository = new();

    public IReadOnlyList<DriveInfo> GetRemovableDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .OrderBy(d => d.Name)
            .ToList();
    }

    public IReadOnlyList<ImportCandidate> ScanDrive(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<ImportCandidate>();

        var results = new List<ImportCandidate>();

        foreach (var path in EnumerateFilesSafe(rootPath))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(path)))
                continue;

            try
            {
                var info = new FileInfo(path);
                results.Add(new ImportCandidate
                {
                    SourcePath = path,
                    SizeBytes = info.Length
                });
            }
            catch
            {
                // Skip files that become unavailable while the card is being scanned.
            }
        }

        return results.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<ImportBatchResult> ImportAsync(
        EventRecord activeEvent,
        IReadOnlyList<ImportCandidate> candidates,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportBatchResult();
        var originalFolder = Path.Combine(activeEvent.RootFolder, "01_Original");
        Directory.CreateDirectory(originalFolder);
        var nextSequence = _repository.GetNextSequence(activeEvent.Id);

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            progress?.Report(new ImportProgress(index + 1, candidates.Count, candidate.FileName, "Checking"));

            try
            {
                var sourceHash = await ComputeSha256Async(candidate.SourcePath, cancellationToken);
                if (_repository.ExistsByHash(activeEvent.Id, sourceHash))
                {
                    result.Duplicates++;
                    progress?.Report(new ImportProgress(index + 1, candidates.Count, candidate.FileName, "Duplicate skipped"));
                    continue;
                }

                var jobNumber = $"{activeEvent.EventCode.ToUpperInvariant()}-{nextSequence:0000}";
                var extension = Path.GetExtension(candidate.SourcePath).ToLowerInvariant();
                var finalPath = Path.Combine(originalFolder, jobNumber + extension);
                var tempPath = finalPath + ".importing";

                progress?.Report(new ImportProgress(index + 1, candidates.Count, candidate.FileName, "Copying"));

                await using (var source = new FileStream(candidate.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                progress?.Report(new ImportProgress(index + 1, candidates.Count, candidate.FileName, "Verifying"));
                var copiedHash = await ComputeSha256Async(tempPath, cancellationToken);
                if (!string.Equals(sourceHash, copiedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Checksum verification failed after copy.");

                File.Move(tempPath, finalPath, overwrite: false);

                _repository.Add(new ImportRecord
                {
                    EventId = activeEvent.Id,
                    JobNumber = jobNumber,
                    OriginalFileName = candidate.FileName,
                    StoredPath = finalPath,
                    Sha256 = sourceHash,
                    FileSize = candidate.SizeBytes,
                    ImportedAt = DateTime.Now,
                    Status = "Imported"
                });

                nextSequence++;
                result.Imported++;
                progress?.Report(new ImportProgress(index + 1, candidates.Count, candidate.FileName, $"Imported as {jobNumber}"));
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"{candidate.FileName}: {ex.Message}");
            }
        }

        return result;
    }

    public IReadOnlyList<ImportRecord> GetImportedJobs(long eventId) => _repository.GetByEvent(eventId);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            string[] directories;

            try { files = Directory.GetFiles(current); }
            catch { files = Array.Empty<string>(); }

            try { directories = Directory.GetDirectories(current); }
            catch { directories = Array.Empty<string>(); }

            foreach (var file in files)
                yield return file;

            foreach (var directory in directories)
                pending.Push(directory);
        }
    }
}

public sealed record ImportProgress(int Current, int Total, string FileName, string Status);

public sealed class ImportBatchResult
{
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; } = new();
}
