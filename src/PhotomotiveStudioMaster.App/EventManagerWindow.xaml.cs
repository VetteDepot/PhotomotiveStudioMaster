using System.Windows;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class EventManagerWindow : Window
{
    private readonly EventRepository _repository = new();

    public EventRecord? SelectedEvent { get; private set; }

    public EventManagerWindow()
    {
        InitializeComponent();
        RefreshEvents();
    }

    private void RefreshEvents()
    {
        EventsGrid.ItemsSource = _repository.GetAll();
    }

    private void NewEvent_Click(object sender, RoutedEventArgs e)
    {
        var window = new NewEventWindow { Owner = this };
        if (window.ShowDialog() == true && window.CreatedEvent is not null)
        {
            SelectedEvent = window.CreatedEvent;
            DialogResult = true;
        }
    }

    private void ResumeEvent_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not EventRecord selected)
        {
            MessageBox.Show("Select an event to resume.", "Event Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedEvent = selected;
        DialogResult = true;
    }
}
