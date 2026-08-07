using Microsoft.Data.Sqlite;
using PhotomotiveStudioMaster.App.Models;

namespace PhotomotiveStudioMaster.App.Services;

public sealed class BackgroundRepository
{
    private readonly string _connectionString;

    public BackgroundRepository()
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
        CREATE TABLE IF NOT EXISTS Backgrounds (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Category TEXT NOT NULL,
            FilePath TEXT NOT NULL,
            ThumbnailPath TEXT NOT NULL,
            Tags TEXT NOT NULL,
            IsFavorite INTEGER NOT NULL DEFAULT 0,
            FileSize INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            LastUsedAt TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Backgrounds_FilePath
            ON Backgrounds(FilePath);
        """;
        command.ExecuteNonQuery();
    }

    public long Add(BackgroundRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText =
        """
        INSERT INTO Backgrounds
        (Name, Category, FilePath, ThumbnailPath, Tags, IsFavorite, FileSize, CreatedAt, LastUsedAt)
        VALUES
        ($name, $category, $filePath, $thumbnailPath, $tags, $favorite, $fileSize, $createdAt, $lastUsedAt);
        SELECT last_insert_rowid();
        """;
        Bind(command, record);
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public void Update(BackgroundRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText =
        """
        UPDATE Backgrounds SET
            Name = $name,
            Category = $category,
            FilePath = $filePath,
            ThumbnailPath = $thumbnailPath,
            Tags = $tags,
            IsFavorite = $favorite,
            FileSize = $fileSize,
            CreatedAt = $createdAt,
            LastUsedAt = $lastUsedAt
        WHERE Id = $id;
        """;
        Bind(command, record);
        command.Parameters.AddWithValue("$id", record.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Backgrounds WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<BackgroundRecord> GetAll()
    {
        var result = new List<BackgroundRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Backgrounds ORDER BY IsFavorite DESC, Name COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(Read(reader));
        return result;
    }

    private static void Bind(SqliteCommand command, BackgroundRecord record)
    {
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$category", record.Category);
        command.Parameters.AddWithValue("$filePath", record.FilePath);
        command.Parameters.AddWithValue("$thumbnailPath", record.ThumbnailPath);
        command.Parameters.AddWithValue("$tags", record.Tags);
        command.Parameters.AddWithValue("$favorite", record.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$fileSize", record.FileSize);
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastUsedAt", record.LastUsedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private static BackgroundRecord Read(SqliteDataReader reader)
    {
        var lastUsedOrdinal = reader.GetOrdinal("LastUsedAt");
        return new BackgroundRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Category = reader.GetString(reader.GetOrdinal("Category")),
            FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
            ThumbnailPath = reader.GetString(reader.GetOrdinal("ThumbnailPath")),
            Tags = reader.GetString(reader.GetOrdinal("Tags")),
            IsFavorite = reader.GetInt64(reader.GetOrdinal("IsFavorite")) == 1,
            FileSize = reader.GetInt64(reader.GetOrdinal("FileSize")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            LastUsedAt = reader.IsDBNull(lastUsedOrdinal) ? null : DateTime.Parse(reader.GetString(lastUsedOrdinal))
        };
    }
}
