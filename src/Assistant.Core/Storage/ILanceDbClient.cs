namespace Assistant.Core.Storage;

/// <summary>LanceDB 向量資料庫客戶端介面（Native Interop 封裝）</summary>
public interface ILanceDbClient
{
    // ── 大綱向量表（document_outlines_vector）──────────────────────────────

    /// <summary>寫入大綱 Embedding 向量</summary>
    Task UpsertOutlineVectorAsync(
        Guid outlineId, string title, float[] vector, CancellationToken ct = default);

    /// <summary>以向量查詢最相似的大綱（Top-K Cosine Similarity）</summary>
    Task<IReadOnlyList<(Guid OutlineId, float Score)>> SearchOutlineVectorsAsync(
        float[] queryVector, int topK, CancellationToken ct = default);

    // ── 知識條目向量表（knowledge_entries_vector）──────────────────────────

    /// <summary>寫入或更新知識條目 Embedding 向量</summary>
    Task UpsertEntryVectorAsync(
        Guid entryId, string title, float[] vector, CancellationToken ct = default);

    /// <summary>以向量查詢最相似的知識條目（Top-K Cosine Similarity）</summary>
    Task<IReadOnlyList<(Guid EntryId, float Score)>> SearchEntryVectorsAsync(
        float[] queryVector, int topK, CancellationToken ct = default);

    /// <summary>刪除知識條目向量（條目刪除時同步清理）</summary>
    Task DeleteEntryVectorAsync(Guid entryId, CancellationToken ct = default);
}
