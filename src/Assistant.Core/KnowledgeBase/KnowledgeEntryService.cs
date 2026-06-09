using Assistant.Core.LlmClient;
using Assistant.Core.Prompts;
using Assistant.Core.Storage;

namespace Assistant.Core.KnowledgeBase;

public sealed class KnowledgeEntryService : IKnowledgeEntryService
{
    private readonly IRelationalRepository _repository;
    private readonly IVersionControlService _versionControl;
    private readonly ILanceDbClient _lanceDbClient;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly IPromptProvider _prompts;

    public KnowledgeEntryService(
        IRelationalRepository repository,
        IVersionControlService versionControl,
        ILanceDbClient lanceDbClient,
        ILlmClientFactory llmClientFactory,
        IPromptProvider prompts)
    {
        _repository = repository;
        _versionControl = versionControl;
        _lanceDbClient = lanceDbClient;
        _llmClientFactory = llmClientFactory;
        _prompts = prompts;
    }

    public async Task<KnowledgeEntry> CreateAsync(string title, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (content == null) throw new ArgumentNullException(nameof(content));

        var entryId = await _repository.InsertEntryAsync(title, content, ct);

        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var vector = await embeddingClient.EmbedAsync(content, ct);
        await _lanceDbClient.UpsertEntryVectorAsync(entryId, title, vector, ct);

        return new KnowledgeEntry
        {
            EntryId = entryId,
            Title = title,
            Content = content,
            Version = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<KnowledgeEntry> MergeAsync(
        Guid entryId, string newDocumentContent, CancellationToken ct = default)
    {
        var operationId = await _repository.StartOperationAsync(
            OperationKind.Merge, entryId, "knowledge-entry-merge", ct);

        try
        {
            var entryData = await _repository.GetEntryAsync(entryId, ct);
            if (entryData == null)
            {
                throw new KeyNotFoundException($"Knowledge entry with ID {entryId} not found.");
            }

            var currentEntry = new KnowledgeEntry
            {
                EntryId = entryData.Value.EntryId,
                Title = entryData.Value.Title,
                Content = entryData.Value.Content,
                Version = entryData.Value.Version,
                UpdatedAt = entryData.Value.UpdatedAt
            };

            var chatClient = _llmClientFactory.CreateChatClient();
            var userMessage =
                $"# Existing knowledge entry\n{currentEntry.Content}\n\n" +
                $"# New source document\n{newDocumentContent}";

            var mergedContent = await chatClient.CompleteAsync(_prompts.KnowledgeEntryMerge, userMessage, ct);

            await _versionControl.ArchiveAsync(currentEntry, ct);

            var nextVersion = currentEntry.Version + 1;
            var now = DateTimeOffset.UtcNow;
            await _repository.UpdateEntryAsync(entryId, mergedContent, nextVersion, now, ct);

            var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
            var newVector = await embeddingClient.EmbedAsync(mergedContent, ct);
            await _lanceDbClient.UpsertEntryVectorAsync(entryId, currentEntry.Title, newVector, ct);

            await _repository.CompleteOperationAsync(operationId, ct);

            return new KnowledgeEntry
            {
                EntryId = entryId,
                Title = currentEntry.Title,
                Content = mergedContent,
                Version = nextVersion,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            await _repository.FailOperationAsync(operationId, ex.Message, ct);
            throw;
        }
    }

    public async Task<KnowledgeEntry?> GetAsync(Guid entryId, CancellationToken ct = default)
    {
        var entryData = await _repository.GetEntryAsync(entryId, ct);
        if (entryData == null)
        {
            return null;
        }

        return new KnowledgeEntry
        {
            EntryId = entryData.Value.EntryId,
            Title = entryData.Value.Title,
            Content = entryData.Value.Content,
            Version = entryData.Value.Version,
            UpdatedAt = entryData.Value.UpdatedAt
        };
    }

    public async Task RollbackAsync(Guid entryId, int targetVersion, CancellationToken ct = default)
    {
        var snapshot = await _versionControl.GetVersionAsync(entryId, targetVersion, ct);
        if (snapshot == null)
        {
            throw new KeyNotFoundException($"Version {targetVersion} of entry {entryId} not found in history.");
        }

        var entryData = await _repository.GetEntryAsync(entryId, ct);
        if (entryData == null)
        {
            throw new KeyNotFoundException($"Knowledge entry with ID {entryId} not found.");
        }

        var currentEntry = new KnowledgeEntry
        {
            EntryId = entryData.Value.EntryId,
            Title = entryData.Value.Title,
            Content = entryData.Value.Content,
            Version = entryData.Value.Version,
            UpdatedAt = entryData.Value.UpdatedAt
        };

        await _versionControl.ArchiveAsync(currentEntry, ct);

        var nextVersion = currentEntry.Version + 1;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpdateEntryAsync(entryId, snapshot.ContentSnapshot, nextVersion, now, ct);

        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var restoredVector = await embeddingClient.EmbedAsync(snapshot.ContentSnapshot, ct);
        await _lanceDbClient.UpsertEntryVectorAsync(entryId, currentEntry.Title, restoredVector, ct);
    }
}
