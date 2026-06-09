using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Core.Config;
using Assistant.Core.Ingestion;
using Assistant.Core.KnowledgeBase;
using Assistant.Core.Search;
using Assistant.Core.LlmClient;
using Assistant.Core.Clustering;

namespace Assistant.App;

/// <summary>IPC 請求格式（前端 → 後端）</summary>
internal sealed record IpcRequest(
    [property: JsonPropertyName("command")] string Command = "",
    [property: JsonPropertyName("requestId")] string RequestId = "",
    [property: JsonPropertyName("payload")] JsonElement Payload = default
);

/// <summary>IPC 回應格式（後端 → 前端）</summary>
internal sealed record IpcResponse<T>(
    [property: JsonPropertyName("requestId")] string RequestId = "",
    [property: JsonPropertyName("success")] bool Success = false,
    [property: JsonPropertyName("data")] T? Data = default,
    [property: JsonPropertyName("error")] string? Error = null
);

// ── 各命令專用 Payload DTO ──────────────────────────────────────────────────

internal sealed record IngestPayload(
    [property: JsonPropertyName("content")] string Content = "",
    [property: JsonPropertyName("source")] string Source = "",
    [property: JsonPropertyName("debug")] bool Debug = false
);

internal sealed record IngestBatchItem(
    [property: JsonPropertyName("content")] string Content = "",
    [property: JsonPropertyName("source")] string Source = ""
);

internal sealed record IngestBatchPayload(
    [property: JsonPropertyName("items")] List<IngestBatchItem> Items = default!,
    [property: JsonPropertyName("debug")] bool Debug = false
);

internal sealed record IngestDebugResult(
    [property: JsonPropertyName("events")] List<LlmDebugEvent> Events
);

internal sealed record IpcDebugEventMessage(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("event")] LlmDebugEvent Event
);

internal sealed record EntryGetPayload(
    [property: JsonPropertyName("entryId")] Guid EntryId
);

internal sealed record RollbackPayload(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("version")] int Version
);

internal sealed record EntryUpdatePayload(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("title")] string Title = "",
    [property: JsonPropertyName("content")] string Content = ""
);

internal sealed record HistoryPayload(
    [property: JsonPropertyName("entryId")] Guid EntryId
);

internal sealed record SearchPayload(
    [property: JsonPropertyName("query")] string Query = ""
);

internal sealed record TestConfigPayload(
    [property: JsonPropertyName("endpoint")] string Endpoint = "",
    [property: JsonPropertyName("apiKey")] string ApiKey = "",
    [property: JsonPropertyName("modelName")] string ModelName = ""
);

internal sealed record TestConfigResult(
    [property: JsonPropertyName("success")] bool Success = false,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null
);

// ── AOT-safe Source-generated Serialization Context ──────────────────────

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse<object>))]
[JsonSerializable(typeof(IpcResponse<KnowledgeEntry>))]
[JsonSerializable(typeof(IpcResponse<List<SearchResult>>))]
[JsonSerializable(typeof(IpcResponse<List<KnowledgeVersion>>))]
[JsonSerializable(typeof(IpcResponse<List<ClusterDetailDto>>))]
[JsonSerializable(typeof(IpcResponse<AppSettings>))]
[JsonSerializable(typeof(IpcResponse<TestConfigResult>))]
[JsonSerializable(typeof(IpcResponse<IngestDebugResult>))]
[JsonSerializable(typeof(IngestDebugResult))]
[JsonSerializable(typeof(IpcDebugEventMessage))]
[JsonSerializable(typeof(LlmDebugEvent))]
[JsonSerializable(typeof(List<LlmDebugEvent>))]
[JsonSerializable(typeof(IngestPayload))]
[JsonSerializable(typeof(IngestBatchPayload))]
[JsonSerializable(typeof(IngestBatchItem))]
[JsonSerializable(typeof(List<IngestBatchItem>))]
[JsonSerializable(typeof(EntryGetPayload))]
[JsonSerializable(typeof(RollbackPayload))]
[JsonSerializable(typeof(EntryUpdatePayload))]
[JsonSerializable(typeof(HistoryPayload))]
[JsonSerializable(typeof(SearchPayload))]
[JsonSerializable(typeof(TestConfigPayload))]
[JsonSerializable(typeof(TestConfigResult))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(KnowledgeEntry))]
[JsonSerializable(typeof(List<SearchResult>))]
[JsonSerializable(typeof(List<KnowledgeVersion>))]
[JsonSerializable(typeof(ClusterDetailDto))]
[JsonSerializable(typeof(ClusterEntryDto))]
[JsonSerializable(typeof(List<ClusterDetailDto>))]
[JsonSerializable(typeof(List<ClusterEntryDto>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class IpcJsonContext : JsonSerializerContext { }

/// <summary>
/// IPC 橋接層：解析前端 postMessage（JSON）→ 路由至對應 Core 服務 → 回傳 JSON 結果。
/// </summary>
internal sealed class IpcBridge(
    IIngestionService ingestion,
    IKnowledgeEntryService knowledge,
    IConfigService config,
    IVersionControlService versionControl,
    IVectorSearchEngine searchEngine,
    ILlmClientFactory llmClientFactory,
    IClusterService clusterService)
{
    private readonly IIngestionService _ingestion = ingestion;
    private readonly IKnowledgeEntryService _knowledge = knowledge;
    private readonly IConfigService _config = config;
    private readonly IVersionControlService _versionControl = versionControl;
    private readonly IVectorSearchEngine _searchEngine = searchEngine;
    private readonly ILlmClientFactory _llmClientFactory = llmClientFactory;
    private readonly IClusterService _clusterService = clusterService;

    public event Action<string>? OutboundMessage;

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
        "ingest.batch" => HandleIngestBatchAsync(request),

        // ── 知識檢索與查詢 ──────────────────────────────────────────────────
        "search" => HandleSearchAsync(request),

        // ── 知識條目查詢 ──────────────────────────────────────────────────
        "entry.get"      => HandleEntryGetAsync(request),
        "entry.update"   => HandleEntryUpdateAsync(request),
        "entry.rollback" => HandleEntryRollbackAsync(request),
        "entry.history"  => HandleEntryHistoryAsync(request),
        "entry.delete"   => HandleEntryDeleteAsync(request),

        // ── 知識分群 ──────────────────────────────────────────────────────
        "cluster.list"      => HandleClusterListAsync(request),
        "cluster.recluster" => HandleClusterReclusterAsync(request),

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
        if (!payload.Debug)
        {
            await _ingestion.IngestAsync(doc);
            return null;
        }

        var trace = new LlmDebugTrace(ev => EmitDebugEvent(req.RequestId, ev));
        using (LlmDebugScope.Begin(trace))
        {
            await _ingestion.IngestAsync(doc);
        }

        return new IngestDebugResult(trace.Events.ToList());
    }

    private async Task<object?> HandleIngestBatchAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.IngestBatchPayload);
        if (payload == null) throw new ArgumentException("Invalid IngestBatchPayload");

        var docs = payload.Items.Select(item => new RawDocument
        {
            Content = item.Content,
            Source = item.Source
        }).ToList();

        if (!payload.Debug)
        {
            await _ingestion.IngestBatchAsync(docs);
            return null;
        }

        var trace = new LlmDebugTrace(ev => EmitDebugEvent(req.RequestId, ev));
        using (LlmDebugScope.Begin(trace))
        {
            await _ingestion.IngestBatchAsync(docs);
        }

        return new IngestDebugResult(trace.Events.ToList());
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

    private async Task<object?> HandleEntryUpdateAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.EntryUpdatePayload);
        if (payload == null) throw new ArgumentException("Invalid EntryUpdatePayload");

        return await _knowledge.UpdateAsync(payload.EntryId, payload.Title, payload.Content);
    }

    private async Task<object?> HandleEntryHistoryAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.HistoryPayload);
        if (payload == null) throw new ArgumentException("Invalid HistoryPayload");

        var history = await _versionControl.GetHistoryAsync(payload.EntryId);
        return history.ToList();
    }

    private async Task<object?> HandleEntryDeleteAsync(IpcRequest req)
    {
        var payload = JsonSerializer.Deserialize(req.Payload, IpcJsonContext.Default.EntryGetPayload);
        if (payload == null) throw new ArgumentException("Invalid EntryGetPayload");

        await _knowledge.DeleteAsync(payload.EntryId);
        return null;
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

    private async Task<object?> HandleClusterListAsync(IpcRequest req)
    {
        var clusters = await _clusterService.GetClustersAsync();
        return clusters.ToList();
    }

    private async Task<object?> HandleClusterReclusterAsync(IpcRequest req)
    {
        await _clusterService.ReclusterAsync();
        return null;
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────

    private static string SuccessResponse(string requestId, object? data)
    {
        if (data is null)
        {
            var response = new IpcResponse<object> { RequestId = requestId, Success = true, Data = null };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<object>), IpcJsonContext.Default);
        }

        if (data is KnowledgeEntry entry)
        {
            var response = new IpcResponse<KnowledgeEntry> { RequestId = requestId, Success = true, Data = entry };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<KnowledgeEntry>), IpcJsonContext.Default);
        }
        if (data is List<SearchResult> searchResults)
        {
            var response = new IpcResponse<List<SearchResult>> { RequestId = requestId, Success = true, Data = searchResults };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<List<SearchResult>>), IpcJsonContext.Default);
        }
        if (data is List<KnowledgeVersion> history)
        {
            var response = new IpcResponse<List<KnowledgeVersion>> { RequestId = requestId, Success = true, Data = history };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<List<KnowledgeVersion>>), IpcJsonContext.Default);
        }
        if (data is AppSettings settings)
        {
            var response = new IpcResponse<AppSettings> { RequestId = requestId, Success = true, Data = settings };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<AppSettings>), IpcJsonContext.Default);
        }
        if (data is TestConfigResult testResult)
        {
            var response = new IpcResponse<TestConfigResult> { RequestId = requestId, Success = true, Data = testResult };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<TestConfigResult>), IpcJsonContext.Default);
        }
        if (data is IngestDebugResult ingestDebugResult)
        {
            var response = new IpcResponse<IngestDebugResult> { RequestId = requestId, Success = true, Data = ingestDebugResult };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<IngestDebugResult>), IpcJsonContext.Default);
        }
        if (data is List<ClusterDetailDto> clusters)
        {
            var response = new IpcResponse<List<ClusterDetailDto>> { RequestId = requestId, Success = true, Data = clusters };
            return JsonSerializer.Serialize(response, typeof(IpcResponse<List<ClusterDetailDto>>), IpcJsonContext.Default);
        }

        throw new NotSupportedException($"Serialization of type {data.GetType().FullName} is not supported in Native AOT.");
    }

    private static string ErrorResponse(string requestId, string error) =>
        JsonSerializer.Serialize(
            new IpcResponse<object> { RequestId = requestId, Success = false, Error = error },
            typeof(IpcResponse<object>),
            IpcJsonContext.Default);

    private void EmitDebugEvent(string requestId, LlmDebugEvent ev)
    {
        var message = new IpcDebugEventMessage("ingest.debug.event", requestId, ev);
        var json = JsonSerializer.Serialize(message, IpcJsonContext.Default.IpcDebugEventMessage);
        OutboundMessage?.Invoke(json);
    }
}
