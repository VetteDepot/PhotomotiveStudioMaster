using System.IO;
using System.Text.RegularExpressions;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public static class EventFolderService
{
    private static readonly string[] ProductionFolders =
    [
        "01_Original",
        "02_Working",
        "03_Masks",
        "04_Extracted",
        "05_Composites",
        "06_Print",
        "07_Web",
        "08_Archive",
        "09_Backup",
        "10_Logs"
    ];

    public static string CreateEventFolders(EventRecord eventRecord, string baseFolder)
    {
        Directory.CreateDirectory(baseFolder);

        var safeName = MakeSafeFolderName(eventRecord.Name);
        var datePrefix = eventRecord.EventDate.ToString("yyyy-MM-dd");
        var eventFolderName = $"{datePrefix}_{safeName}";
        var root = Path.Combine(baseFolder, eventFolderName);

        Directory.CreateDirectory(root);

        foreach (var folder in ProductionFolders)
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }

        return root;
    }

    private static string MakeSafeFolderName(string value)
    {
        var cleaned = Regex.Replace(value.Trim(), "[^A-Za-z0-9 _-]", string.Empty);
        cleaned = Regex.Replace(cleaned, "\\s+", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "Event" : cleaned;
    }
}
