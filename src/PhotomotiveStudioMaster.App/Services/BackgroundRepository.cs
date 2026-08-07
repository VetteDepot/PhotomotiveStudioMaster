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
            LastUsedAt TEXT NULL,
            PixelWidth INTEGER NOT NULL DEFAULT 0,
            PixelHeight INTEGER NOT NULL DEFAULT 0,
            Rating INTEGER NOT NULL DEFAULT 0,
            UseCount INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Backgrounds_FilePath
            ON Backgrounds(FilePath);
        """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "Backgrounds", "PixelWidth", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Backgrounds", "PixelHeight", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Backgrounds", "Rating", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Backgrounds", "UseCount", "INTEGER NOT NULL DEFAULT 0");
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

    public long Add(BackgroundRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText =
        """
        INSERT INTO Backgrounds
        (Name, Category, FilePath, ThumbnailPath, Tags, IsFavorite, FileSize, CreatedAt, LastUsedAt, PixelWidth, PixelHeight, Rating, UseCount)
        VALUES
        ($name, $category, $filePath, $thumbnailPath, $tags, $favorite, $fileSize, $createdAt, $lastUsedAt, $pixelWidth, $pixelHeight, $rating, $useCount);
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
            LastUsedAt = $lastUsedAt,
            PixelWidth = $pixelWidth,
            PixelHeight = $pixelHeight,
            Rating = $rating,
            UseCount = $useCount
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
        command.CommandText = "SELECT * FROM Backgrounds ORDER BY IsFavorite DESC, Rating DESC, Name COLLATE NOCASE;";
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
        command.Parameters.AddWithValue("$pixelWidth", record.PixelWidth);
        command.Parameters.AddWithValue("$pixelHeight", record.PixelHeight);
        command.Parameters.AddWithValue("$rating", Math.Clamp(record.Rating, 0, 5));
        command.Parameters.AddWithValue("$useCount", Math.Max(0, record.UseCount));
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
            LastUsedAt = reader.IsDBNull(lastUsedOrdinal) ? null : DateTime.Parse(reader.GetString(lastUsedOrdinal)),
            PixelWidth = reader.GetInt32(reader.GetOrdinal("PixelWidth")),
            PixelHeight = reader.GetInt32(reader.GetOrdinal("PixelHeight")),
            Rating = reader.GetInt32(reader.GetOrdinal("Rating")),
            UseCount = reader.GetInt32(reader.GetOrdinal("UseCount"))
        };
    }
}
