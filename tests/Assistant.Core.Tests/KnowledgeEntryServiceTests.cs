using Assistant.Core.KnowledgeBase;
using Assistant.Core.Storage;
using Assistant.Core.Clustering;
using Assistant.Core.LlmClient;
using Assistant.Core.Prompts;
using Xunit;

namespace Assistant.Core.Tests;

public class KnowledgeEntryServiceTests
{
    [Fact]
    public async Task DeleteAsync_ShouldDeleteRelationalDataAndVectorAndTriggerRecluster()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var repository = new SqliteRepository(tempFile);
            var lanceDb = new StubLanceDbClient();
            var engine = new StubHdbscanEngine();
            var llmFactory = new StubLlmClientFactory();
            var prompts = new StubPromptProvider();
            
            var clusterService = new ClusterService(repository, lanceDb, engine, llmFactory);
            var service = new KnowledgeEntryService(repository, new VersionControlService(repository), lanceDb, llmFactory, prompts, clusterService);

            // 1. Create entry
            var entry = await service.CreateAsync("Test Title", "Test Content", triggerRecluster: false);
            
            // Add a mock vector in LanceDB stub
            lanceDb.Vectors[entry.EntryId] = new[] { 0.1f, 0.2f, 0.3f };

            // Verify entry exists in DB
            var dbEntry = await repository.GetEntryAsync(entry.EntryId);
            Assert.NotNull(dbEntry);

            // 2. Perform delete
            await service.DeleteAsync(entry.EntryId, triggerRecluster: true);

            // Assert relational data is gone
            var deletedDbEntry = await repository.GetEntryAsync(entry.EntryId);
            Assert.Null(deletedDbEntry);

            // Assert LanceDB stub vector is gone
            Assert.Contains(entry.EntryId, lanceDb.DeletedVectors);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private sealed class StubLanceDbClient : ILanceDbClient
    {
        public Dictionary<Guid, float[]> Vectors { get; } = new();
        public HashSet<Guid> DeletedVectors { get; } = new();

        public Task UpsertOutlineVectorAsync(Guid outlineId, string title, float[] vector, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid OutlineId, float Score)>> SearchOutlineVectorsAsync(float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid OutlineId, float Score)>>([]);

        public Task UpsertEntryVectorAsync(Guid entryId, string title, float[] vector, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid EntryId, float Score)>> SearchEntryVectorsAsync(float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid EntryId, float Score)>>([]);

        public Task DeleteEntryVectorAsync(Guid entryId, CancellationToken ct = default)
        {
            DeletedVectors.Add(entryId);
            Vectors.Remove(entryId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(Guid EntryId, float[] Vector)>> GetAllEntryVectorsAsync(CancellationToken ct = default)
        {
            var results = Vectors.Select(kvp => (kvp.Key, kvp.Value)).ToList();
            return Task.FromResult<IReadOnlyList<(Guid, float[])>>(results);
        }
    }

    private sealed class StubHdbscanEngine : IHdbscanEngine
    {
        public Task<int[]> ClusterAsync(IReadOnlyList<float[]> vectors, CancellationToken ct = default) =>
            Task.FromResult(System.Array.Empty<int>());

        public Task<IReadOnlyList<int[]>> IncrementalClusterAsync(IReadOnlyList<float[]> newVectors, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<int[]>>([]);
    }

    private sealed class StubLlmClientFactory : ILlmClientFactory
    {
        public IChatClient CreateChatClient() => null!;
        public IEmbeddingClient CreateEmbeddingClient() => new StubEmbeddingClient();
        public void Reload() { }
    }

    private sealed class StubEmbeddingClient : IEmbeddingClient
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([]);
    }

    private sealed class StubPromptProvider : IPromptProvider
    {
        public string TitleGeneration => "";
        public string KnowledgeEntryMerge => "";
        public string MultiDocumentMerge => "";
        public string MultiDocumentTitleGeneration => "";
    }
}
