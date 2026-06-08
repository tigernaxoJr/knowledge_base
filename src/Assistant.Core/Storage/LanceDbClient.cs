using Microsoft.Data.Sqlite;

namespace Assistant.Core.Storage;

public sealed class LanceDbClient : ILanceDbClient
{
    private readonly string _connectionString;

    public LanceDbClient(string? dbPath = null)
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
            CREATE TABLE IF NOT EXISTS document_outlines_vector (
                outline_id TEXT PRIMARY KEY,
                title TEXT,
                vector_data BLOB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS knowledge_entries_vector (
                entry_id TEXT PRIMARY KEY,
                title TEXT,
                vector_data BLOB NOT NULL
            );
        ";

        using var command = new SqliteCommand(commandText, connection);
        command.ExecuteNonQuery();
    }

    // ── 大綱向量表 (document_outlines_vector) ──────────────────────────────

    public async Task UpsertOutlineVectorAsync(
        Guid outlineId, string title, float[] vector, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO document_outlines_vector (outline_id, title, vector_data)
            VALUES ($id, $title, $vector)
            ON CONFLICT(outline_id) DO UPDATE SET title = $title, vector_data = $vector;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", outlineId.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$vector", FloatArrayToBytes(vector));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid OutlineId, float Score)>> SearchOutlineVectorsAsync(
        float[] queryVector, int topK, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = "SELECT outline_id, vector_data FROM document_outlines_vector;";
        var candidates = new List<(Guid OutlineId, float[] Vector)>();

        using (var command = new SqliteCommand(query, connection))
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = Guid.Parse(reader.GetString(0));
                var bytes = (byte[])reader.GetValue(1);
                candidates.Add((id, BytesToFloatArray(bytes)));
            }
        }

        var results = candidates
            .Select(c => (c.OutlineId, Score: CalculateCosineSimilarity(queryVector, c.Vector)))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return results;
    }

    // ── 知識條目向量表 (knowledge_entries_vector) ──────────────────────────

    public async Task UpsertEntryVectorAsync(
        Guid entryId, string title, float[] vector, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = @"
            INSERT INTO knowledge_entries_vector (entry_id, title, vector_data)
            VALUES ($id, $title, $vector)
            ON CONFLICT(entry_id) DO UPDATE SET title = $title, vector_data = $vector;
        ";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", entryId.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$vector", FloatArrayToBytes(vector));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid EntryId, float Score)>> SearchEntryVectorsAsync(
        float[] queryVector, int topK, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = "SELECT entry_id, vector_data FROM knowledge_entries_vector;";
        var candidates = new List<(Guid EntryId, float[] Vector)>();

        using (var command = new SqliteCommand(query, connection))
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = Guid.Parse(reader.GetString(0));
                var bytes = (byte[])reader.GetValue(1);
                candidates.Add((id, BytesToFloatArray(bytes)));
            }
        }

        var results = candidates
            .Select(c => (c.EntryId, Score: CalculateCosineSimilarity(queryVector, c.Vector)))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        return results;
    }

    public async Task DeleteEntryVectorAsync(Guid entryId, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var query = "DELETE FROM knowledge_entries_vector WHERE entry_id = $id;";
        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("$id", entryId.ToString());

        await command.ExecuteNonQueryAsync(ct);
    }

    // ── 向量轉換與運算輔助方法 ──────────────────────────────────────────

    private static byte[] FloatArrayToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloatArray(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static float CalculateCosineSimilarity(float[] vecA, float[] vecB)
    {
        if (vecA.Length != vecB.Length || vecA.Length == 0)
        {
            return 0.0f;
        }

        float dotProduct = 0.0f;
        float normA = 0.0f;
        float normB = 0.0f;

        for (int i = 0; i < vecA.Length; i++)
        {
            dotProduct += vecA[i] * vecB[i];
            normA += vecA[i] * vecA[i];
            normB += vecB[i] * vecB[i];
        }

        if (normA == 0.0f || normB == 0.0f)
        {
            return 0.0f;
        }

        return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
