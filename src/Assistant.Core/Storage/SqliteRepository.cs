using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Assistant.Core.Storage;

public sealed class SqliteRepository : IRelationalRepository
{
    private readonly string _connectionString;

    public SqliteRepository(string? dbPath = null)
    {
        dbPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assistant.db");
        _connectionString = $"Data Source={dbPath};Pooling=False";
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

            CREATE TABLE IF NOT EXISTS operation_statuses (
                operation_id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                state TEXT NOT NULL,
                subject_id TEXT,
                source TEXT NOT NULL,
                error_message TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
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

    public async Task<IReadOnlyList<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)>> GetEntriesAsync(
        IEnumerable<Guid> entryIds, CancellationToken ct = default)
    {
        var ids = entryIds.Select(id => id.ToString()).ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<(Guid, string, string, int, DateTimeOffset)>();
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var paramNames = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            paramNames[i] = $"$id{i}";
        }

        var query = $@"
            SELECT entry_id, title, content, version, updated_at
            FROM knowledge_entries
            WHERE entry_id IN ({string.Join(", ", paramNames)});
        ";

        using var command = new SqliteCommand(query, connection);
        for (int i = 0; i < ids.Count; i++)
        {
            command.Parameters.AddWithValue(paramNames[i], ids[i]);
        }

        var list = new List<(Guid, string, string, int, DateTimeOffset)>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = Guid.Parse(reader.GetString(0));
            var title = reader.GetString(1);
            var content = reader.GetString(2);
            var version = reader.GetInt32(3);
            var updatedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);

            list.Add((id, title, content, version, updatedAt));
        }

        return list;
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

    public async Task<(int Version, string ContentSnapshot, DateTimeOffset ArchivedAt)?> GetVersionAsync(
        Guid entryId, int version, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            SELECT version, content_snapshot, archived_at
            FROM knowledge_versions
            WHERE entry_id = $entryId AND version = $version;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$entryId", entryId.ToString());
        command.Parameters.AddWithValue("$version", version);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var ver = reader.GetInt32(0);
            var snapshot = reader.GetString(1);
            var archivedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            return (ver, snapshot, archivedAt);
        }

        return null;
    }

    public async Task<Guid> StartOperationAsync(
        OperationKind kind, Guid? subjectId, string source,
        CancellationToken ct = default)
    {
        var operationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO operation_statuses
                (operation_id, kind, state, subject_id, source, error_message, created_at, updated_at)
            VALUES
                ($operationId, $kind, $state, $subjectId, $source, NULL, $createdAt, $updatedAt);
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$operationId", operationId.ToString());
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$state", OperationState.Running.ToString());
        command.Parameters.AddWithValue("$subjectId", subjectId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);

        await command.ExecuteNonQueryAsync(ct);
        return operationId;
    }

    public Task CompleteOperationAsync(Guid operationId, CancellationToken ct = default) =>
        UpdateOperationStateAsync(operationId, OperationState.Completed, null, ct);

    public Task FailOperationAsync(Guid operationId, string errorMessage, CancellationToken ct = default) =>
        UpdateOperationStateAsync(operationId, OperationState.Failed, errorMessage, ct);

    public async Task<OperationStatus?> GetOperationStatusAsync(Guid operationId, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            SELECT operation_id, kind, state, subject_id, source, error_message, created_at, updated_at
            FROM operation_statuses
            WHERE operation_id = $operationId;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$operationId", operationId.ToString());

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadOperationStatus(reader);
    }

    public async Task<IReadOnlyList<OperationStatus>> GetRecentOperationStatusesAsync(
        OperationKind? kind = null, int limit = 20, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            SELECT operation_id, kind, state, subject_id, source, error_message, created_at, updated_at
            FROM operation_statuses
            WHERE ($kind IS NULL OR kind = $kind)
            ORDER BY updated_at DESC
            LIMIT $limit;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$kind", kind?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var list = new List<OperationStatus>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadOperationStatus(reader));
        }

        return list;
    }

    private async Task UpdateOperationStateAsync(
        Guid operationId, OperationState state, string? errorMessage,
        CancellationToken ct)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            UPDATE operation_statuses
            SET state = $state, error_message = $errorMessage, updated_at = $updatedAt
            WHERE operation_id = $operationId;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$operationId", operationId.ToString());
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    private static OperationStatus ReadOperationStatus(SqliteDataReader reader)
    {
        var subjectIdValue = reader.IsDBNull(3) ? null : reader.GetString(3);

        return new OperationStatus
        {
            OperationId = Guid.Parse(reader.GetString(0)),
            Kind = Enum.Parse<OperationKind>(reader.GetString(1)),
            State = Enum.Parse<OperationState>(reader.GetString(2)),
            SubjectId = Guid.TryParse(subjectIdValue, out var subjectId) ? subjectId : null,
            Source = reader.GetString(4),
            ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
        };
    }
}
