namespace Assistant.Core.Ingestion;

/// <summary>代表一份待導入的原始文件</summary>
public sealed class RawDocument
{
    public Guid DocumentId { get; init; } = Guid.NewGuid();
    public required string Content { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>文件導入服務介面：接收新文件、驅動大綱生成與向量儲存</summary>
public interface IIngestionService
{
    /// <summary>
    /// 導入單份文件：
    /// 1. 呼叫 LLM 生成 400 字大綱
    /// 2. 計算大綱 Embedding 向量
    /// 3. 寫入 LanceDB 向量索引
    /// 4. 驅動路由決策（新建知識條目 or Merge）
    /// </summary>
    Task IngestAsync(RawDocument document, CancellationToken ct = default);

    /// <summary>批次導入（冷啟動 Pipeline 使用）</summary>
    Task IngestBatchAsync(IEnumerable<RawDocument> documents, CancellationToken ct = default);
}
