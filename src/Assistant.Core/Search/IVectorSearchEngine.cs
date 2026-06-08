namespace Assistant.Core.Search;

/// <summary>向量檢索結果</summary>
public sealed class SearchResult
{
    public Guid EntryId { get; init; }
    public string Title { get; init; } = string.Empty;
    public float Score { get; init; }
}

/// <summary>向量搜尋引擎介面：對 LanceDB 執行 Cosine Similarity Top-K 查詢</summary>
public interface IVectorSearchEngine
{
    /// <summary>
    /// 以大綱向量查詢最相似的知識條目。
    /// </summary>
    /// <param name="queryVector">大綱 Embedding 向量</param>
    /// <param name="topK">返回最多幾筆</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<SearchResult>> SearchKnowledgeEntriesAsync(
        float[] queryVector, int topK = 5, CancellationToken ct = default);
}

/// <summary>路由決策閾值常數</summary>
public static class RoutingThresholds
{
    /// <summary>Cosine Similarity 達此值以上視為「相同主題」，執行 Merge</summary>
    public const float MergeThreshold = 0.82f;
}

/// <summary>路由決策結果</summary>
public enum RoutingAction
{
    /// <summary>未找到相似條目，建立新知識條目</summary>
    CreateNew,
    /// <summary>找到相似條目，執行 LLM Merge</summary>
    Merge
}

/// <summary>路由決策服務介面</summary>
public interface IRoutingDecision
{
    /// <summary>
    /// 根據向量搜尋結果決定後續動作（新建 or Merge）。
    /// </summary>
    (RoutingAction Action, SearchResult? BestMatch) Decide(IReadOnlyList<SearchResult> candidates);
}
