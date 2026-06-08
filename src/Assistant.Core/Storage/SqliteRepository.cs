using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Assistant.Core.Storage;

public sealed class SqliteRepository : IRelationalRepository
{
    private readonly string _connectionString;

    public SqliteRepository(string? dbPath = null)
    {
        dbPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assistant.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var commandText = @"
            CREATE TABLE IF NOT EXISTS raw_documents (
                document_id TEXT PRIMARY KEY,
                content TEXT NOT NULL,
                source TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS document_outlines (
                outline_id TEXT PRIMARY KEY,
                document_id TEXT NOT NULL,
                summary TEXT NOT NULL,
                FOREIGN KEY(document_id) REFERENCES raw_documents(document_id)
            );

            CREATE TABLE IF NOT EXISTS knowledge_entries (
                entry_id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                content TEXT NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS knowledge_versions (
                version_id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                content_snapshot TEXT NOT NULL,
                version INTEGER NOT NULL,
                archived_at TEXT NOT NULL,
                FOREIGN KEY(entry_id) REFERENCES knowledge_entries(entry_id)
            );
        ";

        using var command = new SqliteCommand(commandText, connection);
        command.ExecuteNonQuery();
    }

    public async Task InsertDocumentAsync(
        Guid documentId, string content, string source,
        DateTimeOffset createdAt, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO raw_documents (document_id, content, source, created_at)
            VALUES ($id, $content, $source, $createdAt);
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", documentId.ToString());
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("o", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertOutlineAsync(
        Guid outlineId, Guid documentId, string summary,
        CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO document_outlines (outline_id, document_id, summary)
            VALUES ($id, $docId, $summary);
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", outlineId.ToString());
        command.Parameters.AddWithValue("$docId", documentId.ToString());
        command.Parameters.AddWithValue("$summary", summary);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid> InsertEntryAsync(
        string title, string content, CancellationToken ct = default)
    {
        var entryId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO knowledge_entries (entry_id, title, content, version, created_at, updated_at)
            VALUES ($id, $title, $content, 1, $now, $now);
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", entryId.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$now", now);

        await command.ExecuteNonQueryAsync(ct);
        return entryId;
    }

    public async Task UpdateEntryAsync(
        Guid entryId, string content, int version,
        DateTimeOffset updatedAt, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            UPDATE knowledge_entries 
            SET content = $content, version = $version, updated_at = $updatedAt
            WHERE entry_id = $id;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", entryId.ToString());
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("o", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)?> GetEntryAsync(
        Guid entryId, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            SELECT entry_id, title, content, version, updated_at
            FROM knowledge_entries
            WHERE entry_id = $id;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", entryId.ToString());

        using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var id = Guid.Parse(reader.GetString(0));
            var title = reader.GetString(1);
            var content = reader.GetString(2);
            var version = reader.GetInt32(3);
            var updatedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);

            return (id, title, content, version, updatedAt);
        }

        return null;
    }

    public async Task InsertVersionAsync(
        Guid entryId, string contentSnapshot, int version,
        DateTimeOffset archivedAt, CancellationToken ct = default)
    {
        var versionId = Guid.NewGuid().ToString();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO knowledge_versions (version_id, entry_id, content_snapshot, version, archived_at)
            VALUES ($id, $entryId, $snapshot, $version, $archivedAt);
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", versionId);
        command.Parameters.AddWithValue("$entryId", entryId.ToString());
        command.Parameters.AddWithValue("$snapshot", contentSnapshot);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$archivedAt", archivedAt.ToString("o", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(int Version, string ContentSnapshot, DateTimeOffset ArchivedAt)>> GetVersionHistoryAsync(
        Guid entryId, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            SELECT version, content_snapshot, archived_at
            FROM knowledge_versions
            WHERE entry_id = $entryId
            ORDER BY version DESC;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$entryId", entryId.ToString());

        var list = new List<(int, string, DateTimeOffset)>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var version = reader.GetInt32(0);
            var snapshot = reader.GetString(1);
            var archivedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            list.Add((version, snapshot, archivedAt));
        }

        return list;
    }
}
