using System.Text.Json;
using System.Text.Json.Serialization;

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
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class IpcJsonContext : JsonSerializerContext { }

/// <summary>
/// IPC 橋接層：解析前端 postMessage（JSON）→ 路由至對應 Core 服務 → 回傳 JSON 結果。
/// </summary>
internal sealed class IpcBridge
{
    // TODO: 注入 Core 服務
    // private readonly IIngestionService _ingestion;
    // private readonly IKnowledgeEntryService _knowledge;
    // private readonly IConfigService _config;

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

        // ── 知識條目查詢 ──────────────────────────────────────────────────
        "entry.get"      => HandleEntryGetAsync(request),
        "entry.rollback" => HandleEntryRollbackAsync(request),

        // ── 設定管理 ──────────────────────────────────────────────────────
        "config.load"    => HandleConfigLoadAsync(request),
        "config.save"    => HandleConfigSaveAsync(request),
        "config.test"    => HandleConfigTestAsync(request),

        // ── 未知命令 ──────────────────────────────────────────────────────
        _ => throw new NotSupportedException($"Unknown command: {request.Command}")
    };

    // ── 各命令處理器（暫以 stub 實作，等待 Core 服務注入後替換）─────────────

    private Task<object?> HandleIngestAsync(IpcRequest req)
    {
        // TODO: var doc = req.Payload.Deserialize(...); await _ingestion.IngestAsync(doc);
        return Task.FromResult<object?>(null);
    }

    private Task<object?> HandleEntryGetAsync(IpcRequest req)
    {
        // TODO: var id = req.Payload.GetProperty("entryId").GetGuid();
        //       return await _knowledge.GetAsync(id);
        return Task.FromResult<object?>(null);
    }

    private Task<object?> HandleEntryRollbackAsync(IpcRequest req)
    {
        // TODO: implement rollback
        return Task.FromResult<object?>(null);
    }

    private Task<object?> HandleConfigLoadAsync(IpcRequest req)
    {
        // TODO: return await _config.LoadAsync();
        return Task.FromResult<object?>(null);
    }

    private Task<object?> HandleConfigSaveAsync(IpcRequest req)
    {
        // TODO: implement config save
        return Task.FromResult<object?>(null);
    }

    private Task<object?> HandleConfigTestAsync(IpcRequest req)
    {
        // TODO: implement connection test
        return Task.FromResult<object?>(null);
    }

    // ── 輔助方法 ──────────────────────────────────────────────────────────

    private static string SuccessResponse(string requestId, object? data) =>
        JsonSerializer.Serialize(
            new IpcResponse { RequestId = requestId, Success = true, Data = data },
            IpcJsonContext.Default.IpcResponse);

    private static string ErrorResponse(string requestId, string error) =>
        JsonSerializer.Serialize(
            new IpcResponse { RequestId = requestId, Success = false, Error = error },
            IpcJsonContext.Default.IpcResponse);
}
