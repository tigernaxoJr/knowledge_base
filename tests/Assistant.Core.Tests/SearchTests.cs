using Assistant.Core.Search;
using Assistant.Core.Storage;
using Xunit;

namespace Assistant.Core.Tests;

public class SearchTests
{
    [Fact]
    public async Task VectorSearch_ShouldSaveAndRetrieveEntryVectorsBySimilarity()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        try
        {
            var lanceDb = new LanceDbClient(tempFile);

            var entryId1 = Guid.NewGuid();
            var entryId2 = Guid.NewGuid();

            var vec1 = new float[] { 1.0f, 0.0f, 0.0f };
            var vec2 = new float[] { 0.0f, 1.0f, 0.0f };

            await lanceDb.UpsertEntryVectorAsync(entryId1, "Title A", vec1);
            await lanceDb.UpsertEntryVectorAsync(entryId2, "Title B", vec2);

            // Query highly similar to vec1
            var query = new float[] { 0.99f, 0.01f, 0.0f };
            var results = await lanceDb.SearchEntryVectorsAsync(query, topK: 1);

            Assert.Single(results);
            Assert.Equal(entryId1, results[0].EntryId);
            Assert.True(results[0].Score > 0.95f);
        }
        finally
        {
            // Release pooled connection locks so the file can be deleted
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void RoutingDecision_ShouldChooseMergeOrCreateNewBasedOnScore()
    {
        var router = new RoutingDecision();

        // 1. Score above threshold (>= 0.82) -> Merge
        var candidatesMerge = new List<SearchResult>
        {
            new SearchResult { EntryId = Guid.NewGuid(), Title = "A", Score = 0.85f },
            new SearchResult { EntryId = Guid.NewGuid(), Title = "B", Score = 0.5f }
        };
        var decisionMerge = router.Decide(candidatesMerge);
        Assert.Equal(RoutingAction.Merge, decisionMerge.Action);
        Assert.Equal("A", decisionMerge.BestMatch!.Title);

        // 2. Score below threshold (< 0.82) -> CreateNew
        var candidatesCreate = new List<SearchResult>
        {
            new SearchResult { EntryId = Guid.NewGuid(), Title = "A", Score = 0.75f }
        };
        var decisionCreate = router.Decide(candidatesCreate);
        Assert.Equal(RoutingAction.CreateNew, decisionCreate.Action);
        Assert.Null(decisionCreate.BestMatch);
    }
}
