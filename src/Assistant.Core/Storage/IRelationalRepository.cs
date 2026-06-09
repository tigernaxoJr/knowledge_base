namespace Assistant.Core.Storage;

public enum OperationKind
{
    Ingestion,
    BatchIngestion,
    Merge
}

public enum OperationState
{
    Running,
    Completed,
    Failed
}

public sealed class OperationStatus
{
    public Guid OperationId { get; init; }
    public OperationKind Kind { get; init; }
    public OperationState State { get; init; }
    public Guid? SubjectId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IRelationalRepository
{
    Task InsertDocumentAsync(
        Guid documentId, string content, string source,
        DateTimeOffset createdAt, CancellationToken ct = default);

    Task InsertOutlineAsync(
        Guid outlineId, Guid documentId, string summary,
        CancellationToken ct = default);

    Task<Guid> InsertEntryAsync(
        string title, string content, CancellationToken ct = default);

    Task UpdateEntryAsync(
        Guid entryId, string title, string content, int version,
        DateTimeOffset updatedAt, CancellationToken ct = default);

    Task DeleteEntryAsync(
        Guid entryId, CancellationToken ct = default);

    Task<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)?> GetEntryAsync(
        Guid entryId, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)>> GetEntriesAsync(
        IEnumerable<Guid> entryIds, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)>> GetAllEntriesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<(Guid ClusterId, string Name, DateTimeOffset CreatedAt)>> GetClustersAsync(CancellationToken ct = default);

    Task ClearClustersAsync(CancellationToken ct = default);

    Task InsertClusterAsync(Guid clusterId, string name, DateTimeOffset createdAt, CancellationToken ct = default);

    Task DeleteClusterAsync(Guid clusterId, CancellationToken ct = default);

    Task UpdateEntryClusterAsync(Guid entryId, Guid? clusterId, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid EntryId, string Title, int Version, DateTimeOffset UpdatedAt, Guid? ClusterId)>> GetEntriesWithClusterAsync(CancellationToken ct = default);

    Task InsertVersionAsync(
        Guid entryId, string contentSnapshot, int version,
        DateTimeOffset archivedAt, CancellationToken ct = default);

    Task<IReadOnlyList<(int Version, string ContentSnapshot, DateTimeOffset ArchivedAt)>> GetVersionHistoryAsync(
        Guid entryId, CancellationToken ct = default);

    Task<(int Version, string ContentSnapshot, DateTimeOffset ArchivedAt)?> GetVersionAsync(
        Guid entryId, int version, CancellationToken ct = default);

    Task<Guid> StartOperationAsync(
        OperationKind kind, Guid? subjectId, string source,
        CancellationToken ct = default);

    Task CompleteOperationAsync(Guid operationId, CancellationToken ct = default);

    Task FailOperationAsync(Guid operationId, string errorMessage, CancellationToken ct = default);

    Task<OperationStatus?> GetOperationStatusAsync(Guid operationId, CancellationToken ct = default);

    Task<IReadOnlyList<OperationStatus>> GetRecentOperationStatusesAsync(
        OperationKind? kind = null, int limit = 20, CancellationToken ct = default);
}
