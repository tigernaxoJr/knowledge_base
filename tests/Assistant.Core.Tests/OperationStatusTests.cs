using Assistant.Core.Clustering;
using Assistant.Core.Ingestion;
using Assistant.Core.KnowledgeBase;
using Assistant.Core.LlmClient;
using Assistant.Core.Prompts;
using Assistant.Core.Search;
using Assistant.Core.Storage;
using Xunit;

namespace Assistant.Core.Tests;

public class OperationStatusTests
{
    [Fact]
    public async Task IngestAsync_WhenPipelineFails_ShouldRecordFailedOperation()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var repository = new SqliteRepository(tempFile);
            var service = new IngestionService(
                repository,
                new ThrowingOutlineGenerator(),
                new StubLlmClientFactory(),
                new StubLanceDbClient(),
                new StubVectorSearchEngine(),
                new RoutingDecision(),
                new StubKnowledgeEntryService(),
                new StubHdbscanEngine(),
                new DefaultPromptProvider());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IngestAsync(new RawDocument { Content = "source", Source = "unit-test.md" }));

            var statuses = await repository.GetRecentOperationStatusesAsync(OperationKind.Ingestion);
            var status = Assert.Single(statuses);
            Assert.Equal(OperationState.Failed, status.State);
            Assert.Equal("unit-test.md", status.Source);
            Assert.Contains("outline failure", status.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task MergeAsync_WhenEntryDoesNotExist_ShouldRecordFailedOperation()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var repository = new SqliteRepository(tempFile);
            var service = new KnowledgeEntryService(
                repository,
                new VersionControlService(repository),
                new StubLanceDbClient(),
                new StubLlmClientFactory(),
                new DefaultPromptProvider());

            var entryId = Guid.NewGuid();
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.MergeAsync(entryId, "new content"));

            var statuses = await repository.GetRecentOperationStatusesAsync(OperationKind.Merge);
            var status = Assert.Single(statuses);
            Assert.Equal(OperationState.Failed, status.State);
            Assert.Equal(entryId, status.SubjectId);
            Assert.Contains(entryId.ToString(), status.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private sealed class ThrowingOutlineGenerator : IOutlineGenerator
    {
        public Task<string> GenerateOutlineAsync(string documentContent, CancellationToken ct = default) =>
            throw new InvalidOperationException("outline failure");
    }

    private sealed class StubLlmClientFactory : ILlmClientFactory
    {
        public IChatClient CreateChatClient() => new StubChatClient();
        public IEmbeddingClient CreateEmbeddingClient() => new StubEmbeddingClient();
        public void Reload() { }
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default) =>
            Task.FromResult("generated");
    }

    private sealed class StubEmbeddingClient : IEmbeddingClient
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult(new[] { 1.0f, 0.0f, 0.0f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1.0f, 0.0f, 0.0f }).ToList());
    }

    private sealed class StubLanceDbClient : ILanceDbClient
    {
        public Task UpsertOutlineVectorAsync(Guid outlineId, string title, float[] vector, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid OutlineId, float Score)>> SearchOutlineVectorsAsync(
            float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid OutlineId, float Score)>>([]);

        public Task UpsertEntryVectorAsync(Guid entryId, string title, float[] vector, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<(Guid EntryId, float Score)>> SearchEntryVectorsAsync(
            float[] queryVector, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid EntryId, float Score)>>([]);

        public Task DeleteEntryVectorAsync(Guid entryId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubVectorSearchEngine : IVectorSearchEngine
    {
        public Task<IReadOnlyList<SearchResult>> SearchKnowledgeEntriesAsync(
            float[] queryVector, int topK = 5, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SearchResult>>([]);
    }

    private sealed class StubKnowledgeEntryService : IKnowledgeEntryService
    {
        public Task<KnowledgeEntry> CreateAsync(string title, string content, CancellationToken ct = default) =>
            Task.FromResult(new KnowledgeEntry { Title = title, Content = content });

        public Task<KnowledgeEntry> MergeAsync(Guid entryId, string newDocumentContent, CancellationToken ct = default) =>
            Task.FromResult(new KnowledgeEntry { EntryId = entryId, Title = "title", Content = newDocumentContent });

        public Task<KnowledgeEntry?> GetAsync(Guid entryId, CancellationToken ct = default) =>
            Task.FromResult<KnowledgeEntry?>(null);

        public Task RollbackAsync(Guid entryId, int targetVersion, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubHdbscanEngine : IHdbscanEngine
    {
        public Task<int[]> ClusterAsync(IReadOnlyList<float[]> vectors, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<int>());

        public Task<IReadOnlyList<int[]>> IncrementalClusterAsync(
            IReadOnlyList<float[]> newVectors, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<int[]>>([]);
    }
}
