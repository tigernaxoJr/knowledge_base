using Assistant.Core.Storage;

namespace Assistant.Core.KnowledgeBase;

public sealed class VersionControlService(IRelationalRepository repository) : IVersionControlService
{
    private readonly IRelationalRepository _repository = repository;

    public async Task ArchiveAsync(KnowledgeEntry entry, CancellationToken ct = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        await _repository.InsertVersionAsync(
            entry.EntryId,
            entry.Content,
            entry.Version,
            DateTimeOffset.UtcNow,
            ct);
    }

    public async Task<IReadOnlyList<KnowledgeVersion>> GetHistoryAsync(Guid entryId, CancellationToken ct = default)
    {
        var dbHistory = await _repository.GetVersionHistoryAsync(entryId, ct);
        
        var list = new List<KnowledgeVersion>();
        foreach (var item in dbHistory)
        {
            list.Add(new KnowledgeVersion
            {
                EntryId = entryId,
                Version = item.Version,
                ContentSnapshot = item.ContentSnapshot,
                ArchivedAt = item.ArchivedAt
            });
        }

        return list;
    }

    public async Task<KnowledgeVersion?> GetVersionAsync(Guid entryId, int version, CancellationToken ct = default)
    {
        var dbVersion = await _repository.GetVersionAsync(entryId, version, ct);
        if (dbVersion == null)
        {
            return null;
        }

        return new KnowledgeVersion
        {
            EntryId = entryId,
            Version = dbVersion.Value.Version,
            ContentSnapshot = dbVersion.Value.ContentSnapshot,
            ArchivedAt = dbVersion.Value.ArchivedAt
        };
    }
}
