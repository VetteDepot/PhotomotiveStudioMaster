using Microsoft.Data.Sqlite;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class ImportRepository
{
    private readonly string _connectionString;

    public ImportRepository()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(appData, "Photomotive", "StudioMaster");
        Directory.CreateDirectory(dataFolder);
        _connectionString = $"Data Source={Path.Combine(dataFolder, "PhotomotiveStudioMaster.db")}";
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
        """
        CREATE TABLE IF NOT EXISTS Imports (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EventId INTEGER NOT NULL,
            JobNumber TEXT NOT NULL,
            OriginalFileName TEXT NOT NULL,
            StoredPath TEXT NOT NULL,
            Sha256 TEXT NOT NULL,
            FileSize INTEGER NOT NULL,
            ImportedAt TEXT NOT NULL,
            Status TEXT NOT NULL,
            ExtractionPath TEXT NOT NULL DEFAULT ''
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Imports_Event_Sha256
            ON Imports(EventId, Sha256);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Imports_Event_JobNumber
            ON Imports(EventId, JobNumber);
        """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "Imports", "ExtractionPath", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        reader.Close();
        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    public bool ExistsByHash(long eventId, string sha256)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM Imports WHERE EventId = $eventId AND Sha256 = $sha LIMIT 1;";
        command.Parameters.AddWithValue("$eventId", eventId);
        command.Parameters.AddWithValue("$sha", sha256);
        return command.ExecuteScalar() is not null;
    }

    public int GetNextSequence(long eventId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Imports WHERE EventId = $eventId;";
        command.Parameters.AddWithValue("$eventId", eventId);
        return Convert.ToInt32(command.ExecuteScalar()) + 1;
    }

    public void Add(ImportRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText =
        """
        INSERT INTO Imports
        (EventId, JobNumber, OriginalFileName, StoredPath, Sha256, FileSize, ImportedAt, Status, ExtractionPath)
        VALUES
        ($eventId, $jobNumber, $originalFileName, $storedPath, $sha256, $fileSize, $importedAt, $status, $extractionPath);
        """;
        command.Parameters.AddWithValue("$eventId", record.EventId);
        command.Parameters.AddWithValue("$jobNumber", record.JobNumber);
        command.Parameters.AddWithValue("$originalFileName", record.OriginalFileName);
        command.Parameters.AddWithValue("$storedPath", record.StoredPath);
        command.Parameters.AddWithValue("$sha256", record.Sha256);
        command.Parameters.AddWithValue("$fileSize", record.FileSize);
        command.Parameters.AddWithValue("$importedAt", record.ImportedAt.ToString("O"));
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$extractionPath", record.ExtractionPath);
        command.ExecuteNonQuery();
    }

    public void UpdateExtraction(long id, string status, string extractionPath)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Imports SET Status = $status, ExtractionPath = $path WHERE Id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$path", extractionPath ?? string.Empty);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateStatus(long id, string status)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Imports SET Status = $status WHERE Id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ImportRecord> GetByEvent(long eventId)
    {
        var results = new List<ImportRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Imports WHERE EventId = $eventId ORDER BY Id DESC;";
        command.Parameters.AddWithValue("$eventId", eventId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ImportRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                EventId = reader.GetInt64(reader.GetOrdinal("EventId")),
                JobNumber = reader.GetString(reader.GetOrdinal("JobNumber")),
                OriginalFileName = reader.GetString(reader.GetOrdinal("OriginalFileName")),
                StoredPath = reader.GetString(reader.GetOrdinal("StoredPath")),
                Sha256 = reader.GetString(reader.GetOrdinal("Sha256")),
                FileSize = reader.GetInt64(reader.GetOrdinal("FileSize")),
                ImportedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("ImportedAt"))),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                ExtractionPath = reader.GetString(reader.GetOrdinal("ExtractionPath"))
            });
        }
        return results;
    }
}
