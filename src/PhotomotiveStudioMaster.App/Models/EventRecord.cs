namespace PhotomotiveStudioMaster.App.Models;

public sealed class EventRecord
{
    public long Id { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Photographer { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string RootFolder { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }

    public string DisplayDate => EventDate.ToString("MMM d, yyyy");
}
