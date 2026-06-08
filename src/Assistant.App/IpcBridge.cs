using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Core.Config;
using Assistant.Core.Ingestion;
using Assistant.Core.KnowledgeBase;
using Assistant.Core.Search;
using Assistant.Core.LlmClient;

namespace Assistant.App;

/// <summary>IPC 請求格式（前端 → 後端）</summary>
internal sealed class IpcRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

/// <summary>IPC 回應格式（後端 → 前端）</summary>
internal sealed class IpcResponse
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

// ── 各命令專用 Payload DTO ──────────────────────────────────────────────────

internal sealed class IngestPayload
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

internal sealed class EntryGetPayload
{
    [JsonPropertyName("entryId")]
    public Guid EntryId { get; set; }
}

internal sealed class RollbackPayload
{
    [JsonPropertyName("entryId")]
    public Guid EntryId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }
}

internal sealed class HistoryPayload
{
    [JsonPropertyName("entryId")]
    public Guid EntryId { get; set; }
}

internal sealed class SearchPayload
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
}

internal sealed class TestConfigPayload
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;
}

internal sealed class TestConfigResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

// ── AOT-safe Source-generated Serialization Context ──────────────────────

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSerializable(typeof(IngestPayload))]
[JsonSerializable(typeof(EntryGetPayload))]
[JsonSerializable(typeof(RollbackPayload))]
[JsonSerializable(typeof(HistoryPayload))]
[JsonSerializable(typeof(SearchPayload))]
[JsonSerializable(typeof(TestConfigPayload))]
[JsonSerializable(typeof(TestConfigResult))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(KnowledgeEntry))]
[JsonSerializable(typeof(List<SearchResult>))]
[JsonSerializable(typeof(List<KnowledgeVersion>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class IpcJsonContext : JsonSerializerContext { }

/// <summary>
/// IPC 橋接層：解析前端 postMessage（JSON）→ 路由至對應 Core 服務 → 回傳 JSON 結果。
/// </summary>
internal sealed class IpcBridge
{
    private readonly IIngestionService _ingestion;
    private readonly IKnowledgeEntryService _knowledge;
    private readonly IConfigService _config;
    private readonly IVersionControlService _versionControl;
    private readonly IVectorSearchEngine _searchEngine;
    private readonly ILlmClientFactory _llmClientFactory;

    public IpcBridge(
        IIngestionService ingestion,
        IKnowledgeEntryService knowledge,
        IConfigService config,
        IVersionControlService versionControl,
        IVectorSearchEngine searchEngine,
        ILlmClientFactory llmClientFactory)
    {
        _ingestion = ingestion;
        _knowledge = knowledge;
        _config = config;
        _versionControl = versionControl;
        _searchEngine = searchEngine;
        _llmClientFactory = llmClientFactory;
    }

    /// <summary>處理單一 IPC 請求，回傳序列化後的 JSON 回應字串</summary>
    public async Task<string> HandleAsync(string requestJson)
    {
        IpcRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize(requestJson, IpcJsonContext.Default.IpcRequest);
            if (request is null)
                return ErrorResponse(string.Empty, "Invalid JSON");

            var data = await DispatchAsync(request);
            return SuccessResponse(request.RequestId, data);
        }
        catch (Exception ex)
        {
            return ErrorResponse(request?.RequestId ?? string.Empty, ex.Message);
        }
    }

    /// <summary>依 command 名稱路由至對應 Core 方法</summary>
    private Task<object?> DispatchAsync(IpcRequest request) => request.Command switch
    {
        // ── 文件導入 ──────────────────────────────────────────────────────
        "ingest" => HandleIngestAsync(request),

        // ── 知識檢索與查詢 ──────────────────────────────────────────────────
        "search" => HandleSearchAsync(request),

        // ── 知識條目查詢 ──────────────────────────────────────────────────
        "entry.get"      => HandleEntryGetAsync(request),
        "entry.rollback" => HandleEntryRollbackAsync(request),
        "entry.history"  => HandleEntryHistoryAsync(request),

        // ── 設定管理 ──────────────────────────────────────────────────────
        "config.load"    => HandleConfigLoadAsync(request),
        "config.save"    => HandleConfigSaveAsync(request),
        "config.test"    => HandleConfigTestAsync(request),

        // ── 未知命令 ──────────────────────────────────────────────────────
        _ => throw new NotSupportedException($"Unknown command: {request.Command}")
    };

    // ── 各命令處理器 ─────────────────────────────────────────────────────────

    private async Task<object?> HandleIngestAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.IngestPayload);
        if (payload == null) throw new ArgumentException("Invalid IngestPayload");

        var doc = new RawDocument
        {
            Content = payload.Content,
            Source = payload.Source
        };
        await _ingestion.IngestAsync(doc);
        return null;
    }

    private async Task<object?> HandleSearchAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.SearchPayload);
        if (payload == null) throw new ArgumentException("Invalid SearchPayload");

        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var queryVector = await embeddingClient.EmbedAsync(payload.Query);

        var results = await _searchEngine.SearchKnowledgeEntriesAsync(queryVector, topK: 10);
        return results.ToList();
    }

    private async Task<object?> HandleEntryGetAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.EntryGetPayload);
        if (payload == null) throw new ArgumentException("Invalid EntryGetPayload");

        return await _knowledge.GetAsync(payload.EntryId);
    }

    private async Task<object?> HandleEntryRollbackAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.RollbackPayload);
        if (payload == null) throw new ArgumentException("Invalid RollbackPayload");

        await _knowledge.RollbackAsync(payload.EntryId, payload.Version);
        return null;
    }

    private async Task<object?> HandleEntryHistoryAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.HistoryPayload);
        if (payload == null) throw new ArgumentException("Invalid HistoryPayload");

        var history = await _versionControl.GetHistoryAsync(payload.EntryId);
        return history.ToList();
    }

    private async Task<object?> HandleConfigLoadAsync(IpcRequest req)
    {
        return await _config.LoadAsync();
    }

    private async Task<object?> HandleConfigSaveAsync(IpcRequest req)
    {
        var settings = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.AppSettings);
        if (settings == null) throw new ArgumentException("Invalid AppSettings");

        await _config.SaveAsync(settings);
        _llmClientFactory.Reload();
        return null;
    }

    private async Task<object?> HandleConfigTestAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.TestConfigPayload);
        if (payload == null) throw new ArgumentException("Invalid TestConfigPayload");

        var (success, errorMessage) = await _config.TestConnectionAsync(payload.Endpoint, payload.ApiKey, payload.ModelName);
        return new TestConfigResult { Success = success, ErrorMessage = errorMessage };
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────

    private static string SuccessResponse(string requestId, object? data)
    {
        if (data is null)
        {
            var response = new IpcResponse { RequestId = requestId, Success = true, Data = null };
            return JsonSerializer.Serialize(response, IpcJsonContext.Default.IpcResponse);
        }

        string json;
        if (data is KnowledgeEntry entry)
            json = JsonSerializer.Serialize(entry, IpcJsonContext.Default.KnowledgeEntry);
        else if (data is List<SearchResult> searchResults)
            json = JsonSerializer.Serialize(searchResults, IpcJsonContext.Default.ListSearchResult);
        else if (data is List<KnowledgeVersion> history)
            json = JsonSerializer.Serialize(history, IpcJsonContext.Default.ListKnowledgeVersion);
        else if (data is AppSettings settings)
            json = JsonSerializer.Serialize(settings, IpcJsonContext.Default.AppSettings);
        else if (data is TestConfigResult testResult)
            json = JsonSerializer.Serialize(testResult, IpcJsonContext.Default.TestConfigResult);
        else
            throw new NotSupportedException($"Serialization of type {data.GetType().FullName} is not supported in Native AOT.");

        using var doc = JsonDocument.Parse(json);
        var responseWithData = new IpcResponse
        {
            RequestId = requestId,
            Success = true,
            Data = doc.RootElement.Clone()
        };
        return JsonSerializer.Serialize(responseWithData, IpcJsonContext.Default.IpcResponse);
    }

    private static string ErrorResponse(string requestId, string error) =>
        JsonSerializer.Serialize(
            new IpcResponse { RequestId = requestId, Success = false, Error = error },
            IpcJsonContext.Default.IpcResponse);
}
