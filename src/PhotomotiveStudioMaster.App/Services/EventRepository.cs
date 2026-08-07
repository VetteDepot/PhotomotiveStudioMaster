using Microsoft.Data.Sqlite;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class EventRepository
{
    private readonly string _connectionString;

    public EventRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(appData, "Photomotive", "StudioMaster");
        Directory.CreateDirectory(dataFolder);

        var databasePath = Path.Combine(dataFolder, "PhotomotiveStudioMaster.db");
        _connectionString = $"Data Source={databasePath}";
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS Events (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EventCode TEXT NOT NULL,
            Name TEXT NOT NULL,
            EventDate TEXT NOT NULL,
            Location TEXT NOT NULL,
            Photographer TEXT NOT NULL,
            OperatorName TEXT NOT NULL,
            RootFolder TEXT NOT NULL,
            Status TEXT NOT NULL,
            CreatedAt TEXT NOT NULL
        );
        """;
        command.ExecuteNonQuery();
    }

    public long Add(EventRecord eventRecord)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        INSERT INTO Events
        (EventCode, Name, EventDate, Location, Photographer, OperatorName, RootFolder, Status, CreatedAt)
        VALUES
        ($eventCode, $name, $eventDate, $location, $photographer, $operatorName, $rootFolder, $status, $createdAt);
        SELECT last_insert_rowid();
        """;

        command.Parameters.AddWithValue("$eventCode", eventRecord.EventCode);
        command.Parameters.AddWithValue("$name", eventRecord.Name);
        command.Parameters.AddWithValue("$eventDate", eventRecord.EventDate.ToString("O"));
        command.Parameters.AddWithValue("$location", eventRecord.Location);
        command.Parameters.AddWithValue("$photographer", eventRecord.Photographer);
        command.Parameters.AddWithValue("$operatorName", eventRecord.OperatorName);
        command.Parameters.AddWithValue("$rootFolder", eventRecord.RootFolder);
        command.Parameters.AddWithValue("$status", eventRecord.Status);
        command.Parameters.AddWithValue("$createdAt", eventRecord.CreatedAt.ToString("O"));

        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public IReadOnlyList<EventRecord> GetAll()
    {
        var results = new List<EventRecord>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Events ORDER BY EventDate DESC, Id DESC;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEvent(reader));
        }

        return results;
    }

    public EventRecord? GetMostRecentActive()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT * FROM Events WHERE Status = 'Active' ORDER BY EventDate DESC, Id DESC LIMIT 1;";

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEvent(reader) : null;
    }

    private static EventRecord ReadEvent(SqliteDataReader reader)
    {
        return new EventRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            EventCode = reader.GetString(reader.GetOrdinal("EventCode")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            EventDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("EventDate"))),
            Location = reader.GetString(reader.GetOrdinal("Location")),
            Photographer = reader.GetString(reader.GetOrdinal("Photographer")),
            OperatorName = reader.GetString(reader.GetOrdinal("OperatorName")),
            RootFolder = reader.GetString(reader.GetOrdinal("RootFolder")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")))
        };
    }
}
