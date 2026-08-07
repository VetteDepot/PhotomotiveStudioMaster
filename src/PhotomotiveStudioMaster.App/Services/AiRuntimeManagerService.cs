using System.Diagnostics;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class AiRuntimeManagerService
{
    public AiRuntimeSnapshot GetStatus()
    {
        var root = FindRepositoryRoot();
        if (root is null)
            return new AiRuntimeSnapshot(false, false, false, false, false, "Repository AI tools folder was not found.", string.Empty);

        var installer = Path.Combine(root, "tools", "ai", "Install-AI.ps1");
        var worker = Path.Combine(root, "tools", "ai", "extract_vehicle.py");
        var requirements = Path.Combine(root, "tools", "ai", "requirements.txt");
        var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        var model = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".u2net", "u2net.onnx");

        var installerPresent = File.Exists(installer);
        var workerPresent = File.Exists(worker) && File.Exists(requirements);
        var runtimePresent = File.Exists(python);
        var modelPresent = File.Exists(model);
        var ready = installerPresent && workerPresent && runtimePresent && modelPresent;

        var message = ready
            ? "Local AI runtime and U2Net model are installed and ready for offline extraction."
            : "One or more AI runtime components are missing. Use Install / Repair AI Runtime.";

        return new AiRuntimeSnapshot(ready, installerPresent, workerPresent, runtimePresent, modelPresent, message, root);
    }

    public async Task<AiInstallResult> InstallOrRepairAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var snapshot = GetStatus();
        if (string.IsNullOrWhiteSpace(snapshot.RepositoryRoot))
            return new AiInstallResult(false, "Repository AI tools folder was not found.");

        var installer = Path.Combine(snapshot.RepositoryRoot, "tools", "ai", "Install-AI.ps1");
        if (!File.Exists(installer))
            return new AiInstallResult(false, "Install-AI.ps1 was not found in tools\\ai.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = snapshot.RepositoryRoot
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(installer);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report("ERROR: " + e.Data); };

            if (!process.Start())
                return new AiInstallResult(false, "Could not start the AI installer.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            var status = GetStatus();
            if (process.ExitCode == 0 && status.IsReady)
                return new AiInstallResult(true, "AI runtime installation completed successfully.");

            return new AiInstallResult(false,
                process.ExitCode == 0
                    ? "Installer finished, but the runtime is not fully ready yet. Review the installation log."
                    : $"AI installer exited with code {process.ExitCode}. Review the installation log.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AiInstallResult(false, ex.Message);
        }
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++, current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tools", "ai")))
                return current.FullName;
        }

        var working = new DirectoryInfo(Environment.CurrentDirectory);
        for (var i = 0; i < 12 && working is not null; i++, working = working.Parent)
        {
            if (Directory.Exists(Path.Combine(working.FullName, "tools", "ai")))
                return working.FullName;
        }

        return null;
    }
}

public sealed record AiRuntimeSnapshot(
    bool IsReady,
    bool InstallerPresent,
    bool WorkerPresent,
    bool RuntimePresent,
    bool ModelPresent,
    string Message,
    string RepositoryRoot);

public sealed record AiInstallResult(bool Success, string Message);
