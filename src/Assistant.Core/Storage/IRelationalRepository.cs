namespace Assistant.Core.Storage;

/// <summary>SQLite / LiteDB 關聯式資料存取介面（文件元資料、設定、版本歷史）</summary>
public interface IRelationalRepository
{
    // ── raw_documents ──────────────────────────────────────────────────────

    Task InsertDocumentAsync(
        Guid documentId, string content, string source,
        DateTimeOffset createdAt, CancellationToken ct = default);

    // ── document_outlines ──────────────────────────────────────────────────

    Task InsertOutlineAsync(
        Guid outlineId, Guid documentId, string summary,
        CancellationToken ct = default);

    // ── knowledge_entries ──────────────────────────────────────────────────

    Task<Guid> InsertEntryAsync(
        string title, string content, CancellationToken ct = default);

    Task UpdateEntryAsync(
        Guid entryId, string content, int version,
        DateTimeOffset updatedAt, CancellationToken ct = default);

    Task<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)?> GetEntryAsync(
        Guid entryId, CancellationToken ct = default);

    // ── knowledge_versions ────────────────────────────────────────────────

    Task InsertVersionAsync(
        Guid entryId, string contentSnapshot, int version,
        DateTimeOffset archivedAt, CancellationToken ct = default);

    Task<IReadOnlyList<(int Version, string ContentSnapshot, DateTimeOffset ArchivedAt)>> GetVersionHistoryAsync(
        Guid entryId, CancellationToken ct = default);
}
