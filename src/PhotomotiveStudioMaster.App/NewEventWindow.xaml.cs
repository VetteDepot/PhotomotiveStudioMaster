using System.IO;
using System.Windows;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class NewEventWindow : Window
{
    private readonly EventRepository _repository = new();

    public EventRecord? CreatedEvent { get; private set; }

    public NewEventWindow()
    {
        InitializeComponent();
        EventDatePicker.SelectedDate = DateTime.Today;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        StorageFolderBox.Text = Path.Combine(documents, "Photomotive Studio Master", "Events");
    }

    private void CreateEvent_Click(object sender, RoutedEventArgs e)
    {
        var name = EventNameBox.Text.Trim();
        var code = EventCodeBox.Text.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter an event name.", "New Event", MessageBoxButton.OK, MessageBoxImage.Warning);
            EventNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Enter a short event code, such as CBE.", "New Event", MessageBoxButton.OK, MessageBoxImage.Warning);
            EventCodeBox.Focus();
            return;
        }

        var record = new EventRecord
        {
            EventCode = code,
            Name = name,
            EventDate = EventDatePicker.SelectedDate ?? DateTime.Today,
            Location = LocationBox.Text.Trim(),
            Photographer = PhotographerBox.Text.Trim(),
            OperatorName = OperatorBox.Text.Trim(),
            Status = "Active",
            CreatedAt = DateTime.Now
        };

        try
        {
            record.RootFolder = EventFolderService.CreateEventFolders(record, StorageFolderBox.Text);
            record.Id = _repository.Add(record);
            CreatedEvent = record;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The event could not be created.\n\n{ex.Message}",
                "Event Creation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
