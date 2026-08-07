using System.Windows;

namespace PhotomotiveStudioMaster.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void CreateEvent_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Event creation will be added in the next verified milestone.",
            "Photomotive Studio Master",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
