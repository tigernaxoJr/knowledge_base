using Assistant.Core.Storage;

namespace Assistant.Core.Search;

public sealed class VectorSearchEngine(ILanceDbClient lanceDbClient, IRelationalRepository relationalRepository) : IVectorSearchEngine
{
    private readonly ILanceDbClient _lanceDbClient = lanceDbClient;
    private readonly IRelationalRepository _relationalRepository = relationalRepository;

    public async Task<IReadOnlyList<SearchResult>> SearchKnowledgeEntriesAsync(
        float[] queryVector, int topK = 5, CancellationToken ct = default)
    {
        var vectorMatches = await _lanceDbClient.SearchEntryVectorsAsync(queryVector, topK, ct);
        if (vectorMatches.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        var entryIds = vectorMatches.Select(m => m.EntryId).ToList();
        var entries = await _relationalRepository.GetEntriesAsync(entryIds, ct);
        var entryMap = entries.ToDictionary(e => e.EntryId);

        var results = new List<SearchResult>();
        foreach (var match in vectorMatches)
        {
            if (entryMap.TryGetValue(match.EntryId, out var entry))
            {
                results.Add(new SearchResult
                {
                    EntryId = match.EntryId,
                    Title = entry.Title,
                    Score = match.Score
                });
            }
        }

        return results;
    }
}
