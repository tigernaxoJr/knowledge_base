using Assistant.Core.Storage;

namespace Assistant.Core.Search;

public sealed class VectorSearchEngine : IVectorSearchEngine
{
    private readonly ILanceDbClient _lanceDbClient;
    private readonly IRelationalRepository _relationalRepository;

    public VectorSearchEngine(ILanceDbClient lanceDbClient, IRelationalRepository relationalRepository)
    {
        _lanceDbClient = lanceDbClient;
        _relationalRepository = relationalRepository;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchKnowledgeEntriesAsync(
        float[] queryVector, int topK = 5, CancellationToken ct = default)
    {
        var vectorMatches = await _lanceDbClient.SearchEntryVectorsAsync(queryVector, topK, ct);
        var results = new List<SearchResult>();

        foreach (var match in vectorMatches)
        {
            var entry = await _relationalRepository.GetEntryAsync(match.EntryId, ct);
            if (entry != null)
            {
                results.Add(new SearchResult
                {
                    EntryId = match.EntryId,
                    Title = entry.Value.Title,
                    Score = match.Score
                });
            }
        }

        return results;
    }
}
