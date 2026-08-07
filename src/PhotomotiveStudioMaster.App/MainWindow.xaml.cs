using System.Windows;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class MainWindow : Window
{
    private readonly EventRepository _repository = new();
    private EventRecord? _activeEvent;

    public MainWindow()
    {
        InitializeComponent();
        SetActiveEvent(_repository.GetMostRecentActive());
    }

    private void CreateEvent_Click(object sender, RoutedEventArgs e)
    {
        var window = new NewEventWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            SetActiveEvent(window.CreatedEvent);
        }
    }

    private void EventManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new EventManagerWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            SetActiveEvent(window.SelectedEvent);
        }
    }

    private void SetActiveEvent(EventRecord? eventRecord)
    {
        _activeEvent = eventRecord;
        ActiveEventText.Text = _activeEvent is null
            ? "No active event"
            : $"{_activeEvent.EventCode} • {_activeEvent.Name}";
    }
}
