using Assistant.Core.Storage;

namespace Assistant.Core.KnowledgeBase;

public sealed class VersionControlService : IVersionControlService
{
    private readonly IRelationalRepository _repository;

    public VersionControlService(IRelationalRepository repository)
    {
        _repository = repository;
    }

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
        var history = await GetHistoryAsync(entryId, ct);
        return history.FirstOrDefault(h => h.Version == version);
    }
}
