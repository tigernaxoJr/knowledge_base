using Assistant.Core.Clustering;
using Assistant.Core.LlmClient;
using Assistant.Core.Storage;
using Xunit;

namespace Assistant.Core.Tests;

public class ClusterServiceTests
{
    [Fact]
    public async Task ReclusterAsync_ShouldClusterEntriesAndInheritOrGenerateNames()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var repository = new SqliteRepository(tempFile);
            var lanceDb = new StubLanceDbClient();
            var engine = new StubHdbscanEngine();
            var llmFactory = new StubLlmClientFactory();

            var service = new ClusterService(repository, lanceDb, engine, llmFactory);

            // 1. Insert knowledge entries
            var id1 = await repository.InsertEntryAsync("Docker Volume Setup", "Content 1");
            var id2 = await repository.InsertEntryAsync("Docker Network Config", "Content 2");
            var id3 = await repository.InsertEntryAsync("Vue Routing Guide", "Content 3");

            // Mock vectors in LanceDB stub
            lanceDb.Vectors[id1] = new[] { 1.0f, 0.0f, 0.0f };
            lanceDb.Vectors[id2] = new[] { 0.98f, 0.01f, 0.0f };
            lanceDb.Vectors[id3] = new[] { 0.0f, 1.0f, 0.0f };

            // Mock DBSCAN engine: id1 and id2 are cluster 0, id3 is cluster 1
            engine.ClusterLabels = new[] { 0, 0, 1 };

            // Mock LLM chat client output
            llmFactory.GeneratedNames.Enqueue("Docker 容器");
            llmFactory.GeneratedNames.Enqueue("Vue 前端");

            // 2. Perform Recluster
            await service.ReclusterAsync();

            // 3. Assert clusters in DB
            var clusters = await repository.GetClustersAsync();
            Assert.Equal(2, clusters.Count);
            
            var dockerCluster = clusters.FirstOrDefault(c => c.Name == "Docker 容器");
            var vueCluster = clusters.FirstOrDefault(c => c.Name == "Vue 前端");
            Assert.NotEqual(Guid.Empty, dockerCluster.ClusterId);
            Assert.NotEqual(Guid.Empty, vueCluster.ClusterId);

            var entries = await repository.GetEntriesWithClusterAsync();
            var entry1 = entries.First(e => e.EntryId == id1);
            var entry2 = entries.First(e => e.EntryId == id2);
            var entry3 = entries.First(e => e.EntryId == id3);

            Assert.Equal(dockerCluster.ClusterId, entry1.ClusterId);
            Assert.Equal(dockerCluster.ClusterId, entry2.ClusterId);
            Assert.Equal(vueCluster.ClusterId, entry3.ClusterId);

            // 4. Test member-change detection: Add a new entry to the Docker cluster, and recluster
            var id4 = await repository.InsertEntryAsync("Docker Registry Config", "Content 4");
            lanceDb.Vectors[id4] = new[] { 0.95f, 0.02f, 0.0f };
            
            // DBSCAN labels: id1, id2, id4 are cluster 0. id3 is cluster 1.
            engine.ClusterLabels = new[] { 0, 0, 1, 0 };

            // Docker cluster gains id4 → members changed → LLM regenerates name
            // Vue cluster stays {id3} → members unchanged → name inherited
            llmFactory.GeneratedNames.Clear();
            llmFactory.GeneratedNames.Enqueue("Docker 部署技術");

            await service.ReclusterAsync();

            var clustersAfter = await repository.GetClustersAsync();
            Assert.Equal(2, clustersAfter.Count);
            
            // Docker cluster should have the REGENERATED name since members changed
            var dockerClusterAfter = clustersAfter.FirstOrDefault(c => c.ClusterId == dockerCluster.ClusterId);
            Assert.NotEqual(Guid.Empty, dockerClusterAfter.ClusterId);
            Assert.Equal("Docker 部署技術", dockerClusterAfter.Name);

            // Vue cluster should keep the inherited name since members are unchanged
            var vueClusterAfter = clustersAfter.FirstOrDefault(c => c.ClusterId == vueCluster.ClusterId);
            Assert.Equal("Vue 前端", vueClusterAfter.Name);

            var entriesAfter = await repository.GetEntriesWithClusterAsync();
            var entry4 = entriesAfter.First(e => e.EntryId == id4);
            Assert.Equal(dockerCluster.ClusterId, entry4.ClusterId);
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

        public Task UpsertOutlineVectorAsync(Guid outlineId, string title, float[] vector, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid OutlineId, float Score)>> SearchOutlineVectorsAsync(float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid OutlineId, float Score)>>([]);

        public Task UpsertEntryVectorAsync(Guid entryId, string title, float[] vector, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid EntryId, float Score)>> SearchEntryVectorsAsync(float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid EntryId, float Score)>>([]);

        public Task DeleteEntryVectorAsync(Guid entryId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid EntryId, float[] Vector)>> GetAllEntryVectorsAsync(CancellationToken ct = default)
        {
            var results = Vectors.Select(kvp => (kvp.Key, kvp.Value)).ToList();
            return Task.FromResult<IReadOnlyList<(Guid, float[])>>(results);
        }
    }

    private sealed class StubHdbscanEngine : IHdbscanEngine
    {
        public int[] ClusterLabels { get; set; } = Array.Empty<int>();

        public Task<int[]> ClusterAsync(IReadOnlyList<float[]> vectors, CancellationToken ct = default) =>
            Task.FromResult(ClusterLabels);

        public Task<IReadOnlyList<int[]>> IncrementalClusterAsync(IReadOnlyList<float[]> newVectors, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<int[]>>([]);
    }

    private sealed class StubLlmClientFactory : ILlmClientFactory
    {
        public Queue<string> GeneratedNames { get; } = new();

        public IChatClient CreateChatClient() => new StubChatClient(GeneratedNames);
        public IEmbeddingClient CreateEmbeddingClient() => null!;
        public void Reload() { }
    }

    private sealed class StubChatClient(Queue<string> generatedNames) : IChatClient
    {
        private readonly Queue<string> _names = generatedNames;

        public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            if (_names.Count > 0)
            {
                return Task.FromResult(_names.Dequeue());
            }
            return Task.FromResult("Default Topic");
        }
    }
}
