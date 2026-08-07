using System.Windows;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class AiRuntimeManagerWindow : Window
{
    private readonly AiRuntimeManagerService _service = new();

    public AiRuntimeManagerWindow()
    {
        InitializeComponent();
        RefreshStatus();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        var status = _service.GetStatus();
        OverallStatusText.Text = status.IsReady ? "● Local AI Ready" : "○ AI Setup Required";
        OverallDetailText.Text = status.Message;
        InstallerStatusText.Text = Format(status.InstallerPresent, "Available", "Missing");
        WorkerStatusText.Text = Format(status.WorkerPresent, "Available", "Missing");
        PythonStatusText.Text = Format(status.RuntimePresent, "Installed", "Not installed");
        ModelStatusText.Text = Format(status.ModelPresent, "Downloaded", "Not downloaded");
        OfflineStatusText.Text = status.IsReady ? "READY" : "NOT READY";
        InstallButton.Content = status.IsReady ? "REPAIR / VERIFY AI RUNTIME" : "INSTALL / REPAIR AI RUNTIME";
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "The first-time setup downloads Python packages and the U2Net model. Internet access is required only during setup. Continue?",
            "Install Local AI Runtime",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.Yes)
            return;

        InstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        LogBox.Clear();
        AppendLog("Starting AI runtime setup...");

        var progress = new Progress<string>(AppendLog);

        try
        {
            var result = await _service.InstallOrRepairAsync(progress);
            AppendLog(result.Message);
            RefreshStatus();

            MessageBox.Show(
                result.Message,
                result.Success ? "AI Runtime Ready" : "AI Runtime Setup",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            MessageBox.Show(ex.Message, "AI Runtime Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallButton.IsEnabled = true;
            RefreshButton.IsEnabled = true;
            RefreshStatus();
        }
    }

    private void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private static string Format(bool value, string yes, string no) => value ? "✓ " + yes : "✕ " + no;
}
