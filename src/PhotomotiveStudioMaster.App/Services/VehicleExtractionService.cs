using System.Diagnostics;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class VehicleExtractionService
{
    private static readonly HashSet<string> SupportedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff"
    };

    private readonly ImportRepository _repository = new();

    public AiRuntimeStatus GetRuntimeStatus()
    {
        var root = FindRepositoryRoot();
        if (root is null)
            return new AiRuntimeStatus(false, "Repository tools folder not found.");

        var script = Path.Combine(root, "tools", "ai", "extract_vehicle.py");
        if (!File.Exists(script))
            return new AiRuntimeStatus(false, "Extraction worker script not found.");

        var python = ResolvePython(root);
        if (python is null)
            return new AiRuntimeStatus(false, "Local AI environment is not installed. Run tools\\ai\\Install-AI.ps1.");

        return new AiRuntimeStatus(true, $"Local AI ready: {python}");
    }

    public async Task<ExtractionResult> ExtractAsync(
        EventRecord activeEvent,
        ImportRecord job,
        CancellationToken cancellationToken = default)
    {
        if (!SupportedInputExtensions.Contains(Path.GetExtension(job.StoredPath)))
        {
            return ExtractionResult.Failed(
                "This checkpoint extracts JPEG, PNG, and TIFF files. RAW preview extraction will be added next.");
        }

        var root = FindRepositoryRoot();
        if (root is null)
            return ExtractionResult.Failed("Could not locate the repository AI tools folder.");

        var script = Path.Combine(root, "tools", "ai", "extract_vehicle.py");
        var python = ResolvePython(root);
        if (python is null || !File.Exists(script))
            return ExtractionResult.Failed("Local AI is not installed. Run tools\\ai\\Install-AI.ps1 first.");

        var outputFolder = Path.Combine(activeEvent.RootFolder, "04_Extracted");
        Directory.CreateDirectory(outputFolder);
        var outputPath = Path.Combine(outputFolder, job.JobNumber + ".png");

        _repository.UpdateExtraction(job.Id, "Extracting", job.ExtractionPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add(job.StoredPath);
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add("u2net");

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _repository.UpdateExtraction(job.Id, "Extraction Error", string.Empty);
                var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return ExtractionResult.Failed(string.IsNullOrWhiteSpace(message)
                    ? $"Extraction worker exited with code {process.ExitCode}."
                    : message.Trim());
            }

            _repository.UpdateExtraction(job.Id, "Extracted", outputPath);
            return ExtractionResult.Succeeded(outputPath);
        }
        catch (OperationCanceledException)
        {
            _repository.UpdateExtraction(job.Id, "Imported", string.Empty);
            throw;
        }
        catch (Exception ex)
        {
            _repository.UpdateExtraction(job.Id, "Extraction Error", string.Empty);
            return ExtractionResult.Failed(ex.Message);
        }
    }

    private static string? ResolvePython(string root)
    {
        var configured = Environment.GetEnvironmentVariable("PHOTOMOTIVE_AI_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var venvPython = Path.Combine(root, ".venv", "Scripts", "python.exe");
        return File.Exists(venvPython) ? venvPython : null;
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "ai")))
                return current.FullName;
        }

        var working = new DirectoryInfo(Environment.CurrentDirectory);
        for (var i = 0; i < 10 && working is not null; i++, working = working.Parent)
        {
            if (Directory.Exists(Path.Combine(working.FullName, "tools", "ai")))
                return working.FullName;
        }

        return null;
    }
}

public sealed record AiRuntimeStatus(bool IsReady, string Message);

public sealed record ExtractionResult(bool Success, string OutputPath, string ErrorMessage)
{
    public static ExtractionResult Succeeded(string outputPath) => new(true, outputPath, string.Empty);
    public static ExtractionResult Failed(string message) => new(false, string.Empty, message);
}
