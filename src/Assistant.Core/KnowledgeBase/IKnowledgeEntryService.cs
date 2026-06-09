namespace Assistant.Core.KnowledgeBase;

/// <summary>知識條目資料模型</summary>
public sealed class KnowledgeEntry
{
    public Guid EntryId { get; init; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Content { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>知識條目 CRUD 與版本控制服務介面</summary>
public interface IKnowledgeEntryService
{
    /// <summary>建立新知識條目（Case A：新主題）</summary>
    Task<KnowledgeEntry> CreateAsync(string title, string content, bool triggerRecluster = true, CancellationToken ct = default);

    /// <summary>
    /// 以 LLM 將新文件資訊 Merge 進既有知識條目（Case B：相同主題更新）。
    /// 自動備份舊版本至 knowledge_versions。
    /// </summary>
    Task<KnowledgeEntry> MergeAsync(
        Guid entryId, string newDocumentContent, bool triggerRecluster = true, CancellationToken ct = default);

    /// <summary>取得單一知識條目</summary>
    Task<KnowledgeEntry?> GetAsync(Guid entryId, CancellationToken ct = default);

    /// <summary>Rollback 至指定版本</summary>
    Task RollbackAsync(Guid entryId, int targetVersion, bool triggerRecluster = true, CancellationToken ct = default);

    /// <summary>直接編輯既有知識條目的標題與內容，自動備份舊版本</summary>
    Task<KnowledgeEntry> UpdateAsync(Guid entryId, string title, string content, bool triggerRecluster = true, CancellationToken ct = default);

    /// <summary>刪除知識條目，包含其所有歷史版本與向量，並可觸發重新分群</summary>
    Task DeleteAsync(Guid entryId, bool triggerRecluster = true, CancellationToken ct = default);
}
