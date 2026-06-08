namespace Assistant.Core.KnowledgeBase;

/// <summary>歷史版本快照</summary>
public sealed class KnowledgeVersion
{
    public long VersionId { get; init; }
    public Guid EntryId { get; init; }
    public required string ContentSnapshot { get; init; }
    public int Version { get; init; }
    public DateTimeOffset ArchivedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>版本控制服務介面：知識條目歷史版本備份與還原</summary>
public interface IVersionControlService
{
    /// <summary>將當前條目內容備份為歷史版本</summary>
    Task ArchiveAsync(KnowledgeEntry entry, CancellationToken ct = default);

    /// <summary>取得指定條目的所有歷史版本清單</summary>
    Task<IReadOnlyList<KnowledgeVersion>> GetHistoryAsync(
        Guid entryId, CancellationToken ct = default);

    /// <summary>取得指定版本的快照內容</summary>
    Task<KnowledgeVersion?> GetVersionAsync(
        Guid entryId, int version, CancellationToken ct = default);
}
