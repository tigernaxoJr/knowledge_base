using Apache.Arrow;
using Apache.Arrow.Types;
using lancedb;

namespace Assistant.Core.Storage;

public sealed class LanceDbClient : ILanceDbClient, IDisposable
{
    private readonly string _dbDirectory;
    private Connection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;

    public LanceDbClient(string? dbPath = null)
    {
        if (dbPath == null)
        {
            _dbDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lancedb");
        }
        else
        {
            if (dbPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(dbPath);
                var name = Path.GetFileNameWithoutExtension(dbPath);
                _dbDirectory = Path.Combine(dir ?? AppDomain.CurrentDomain.BaseDirectory, $"{name}_lancedb");
            }
            else
            {
                _dbDirectory = dbPath;
            }
        }
    }

    private async Task<Connection> GetConnectionAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_connection != null)
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection == null)
            {
                if (!Directory.Exists(_dbDirectory))
                {
                    Directory.CreateDirectory(_dbDirectory);
                }
                var conn = new Connection();
                await conn.Connect(_dbDirectory);
                _connection = conn;
            }
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<lancedb.Table> GetOrCreateTableAsync(Connection conn, string tableName, int vectorDim, CancellationToken ct)
    {
        var tables = await conn.TableNames();
        if (tables.Contains(tableName))
        {
            return await conn.OpenTable(tableName);
        }

        var schema = CreateSchema(tableName, vectorDim);
        return await conn.CreateTable(tableName, new CreateTableOptions { Schema = schema });
    }

    private Schema CreateSchema(string tableName, int vectorDim)
    {
        var vectorField = new Field("vector", new FixedSizeListType(FloatType.Default, vectorDim), nullable: false);

        if (tableName == "document_outlines_vector")
        {
            return new Schema.Builder()
                .Field(new Field("outline_id", StringType.Default, nullable: false))
                .Field(new Field("title", StringType.Default, nullable: true))
                .Field(vectorField)
                .Build();
        }
        else // knowledge_entries_vector
        {
            return new Schema.Builder()
                .Field(new Field("entry_id", StringType.Default, nullable: false))
                .Field(new Field("title", StringType.Default, nullable: true))
                .Field(vectorField)
                .Build();
        }
    }

    // ── 大綱向量表 (document_outlines_vector) ──────────────────────────────

    public Task UpsertOutlineVectorAsync(
        Guid outlineId, string title, float[] vector, CancellationToken ct = default)
    {
        return UpsertVectorInternalAsync("document_outlines_vector", outlineId, title, vector, ct);
    }

    public async Task<IReadOnlyList<(Guid OutlineId, float Score)>> SearchOutlineVectorsAsync(
        float[] queryVector, int topK, CancellationToken ct = default)
    {
        var results = await SearchVectorsInternalAsync("document_outlines_vector", "outline_id", queryVector, topK, ct);
        return results.Select(r => (r.Id, r.Score)).ToList();
    }

    // ── 知識條目向量表 (knowledge_entries_vector) ──────────────────────────

    public Task UpsertEntryVectorAsync(
        Guid entryId, string title, float[] vector, CancellationToken ct = default)
    {
        return UpsertVectorInternalAsync("knowledge_entries_vector", entryId, title, vector, ct);
    }

    public async Task<IReadOnlyList<(Guid EntryId, float Score)>> SearchEntryVectorsAsync(
        float[] queryVector, int topK, CancellationToken ct = default)
    {
        var results = await SearchVectorsInternalAsync("knowledge_entries_vector", "entry_id", queryVector, topK, ct);
        return results.Select(r => (r.Id, r.Score)).ToList();
    }

    private async Task UpsertVectorInternalAsync(
        string tableName, Guid id, string title, float[] vector, CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct);
        var table = await GetOrCreateTableAsync(connection, tableName, vector.Length, ct);

        // Delete if exists to avoid duplicates
        var idField = tableName == "document_outlines_vector" ? "outline_id" : "entry_id";
        await table.Delete($"{idField} = '{id}'");

        var schema = CreateSchema(tableName, vector.Length);
        
        var idArray = new StringArray.Builder().Append(id.ToString()).Build();
        var titleArray = new StringArray.Builder().Append(title).Build();

        var listBuilder = new FixedSizeListArray.Builder(FloatType.Default, vector.Length);
        listBuilder.Append();
        var floatBuilder = (FloatArray.Builder)listBuilder.ValueBuilder;
        foreach (var val in vector)
        {
            floatBuilder.Append(val);
        }
        var vectorArray = listBuilder.Build();

        using var batch = new RecordBatch(schema, new IArrowArray[] { idArray, titleArray, vectorArray }, 1);
        await table.Add(batch);
    }

    private async Task<IReadOnlyList<(Guid Id, float Score)>> SearchVectorsInternalAsync(
        string tableName, string idFieldName, float[] queryVector, int topK, CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct);
        var table = await GetOrCreateTableAsync(connection, tableName, queryVector.Length, ct);

        using var reader = await table.Query()
            .NearestTo(queryVector)
            .Limit(topK)
            .ToBatches();

        var results = new List<(Guid Id, float Score)>();

        await foreach (var batch in reader)
        {
            var idColumn = batch.Column(idFieldName) as StringArray;
            var distanceColumn = batch.Column("_distance") as FloatArray;

            if (idColumn != null && distanceColumn != null)
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    var idStr = idColumn.GetString(i);
                    if (Guid.TryParse(idStr, out var id))
                    {
                        var distance = distanceColumn.GetValue(i) ?? 0.0f;
                        // Translate cosine distance (1 - similarity) to similarity score
                        var score = 1.0f - distance;
                        results.Add((id, score));
                    }
                }
            }
        }

        return results.OrderByDescending(r => r.Score).Take(topK).ToList();
    }

    public async Task DeleteEntryVectorAsync(Guid entryId, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct);
        var tables = await connection.TableNames();
        if (!tables.Contains("knowledge_entries_vector"))
        {
            return;
        }

        var table = await connection.OpenTable("knowledge_entries_vector");
        await table.Delete($"entry_id = '{entryId}'");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _connection?.Dispose();
        _lock.Dispose();
        _isDisposed = true;
    }
}
