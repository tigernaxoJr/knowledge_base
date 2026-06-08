using Assistant.Core.LlmClient;
using Assistant.Core.Storage;

namespace Assistant.Core.KnowledgeBase;

public sealed class KnowledgeEntryService : IKnowledgeEntryService
{
    private readonly IRelationalRepository _repository;
    private readonly IVersionControlService _versionControl;
    private readonly ILanceDbClient _lanceDbClient;
    private readonly ILlmClientFactory _llmClientFactory;

    private const string MergeSystemPrompt =
        "你是一個高效率的知識庫管理助手。你的任務是將「新文件」的資訊，增量且有機地融合進「既有的知識條目」中。\n" +
        "請遵循以下規則：\n" +
        "1. 必須保留既有條目中所有依然正確且有價值的核心內容，避免重要資訊遺失。\n" +
        "2. 將新文件中的新資訊、補充說明、更正內容或時序更新編排進來，保持整篇文章的連貫性與結構化。\n" +
        "3. 輸出必須是一篇結構完整的 Markdown 知識條目文章，不得含有「同原文章」、「此處省略」等簡約標記。\n" +
        "4. 不要包含任何自我介紹或解釋，直接輸出融合後完整的 Markdown 文章。";

    public KnowledgeEntryService(
        IRelationalRepository repository,
        IVersionControlService versionControl,
        ILanceDbClient lanceDbClient,
        ILlmClientFactory llmClientFactory)
    {
        _repository = repository;
        _versionControl = versionControl;
        _lanceDbClient = lanceDbClient;
        _llmClientFactory = llmClientFactory;
    }

    public async Task<KnowledgeEntry> CreateAsync(string title, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (content == null) throw new ArgumentNullException(nameof(content));

        // 1. Insert into Relational Database
        var entryId = await _repository.InsertEntryAsync(title, content, ct);

        // 2. Generate and Insert Embedding Vector
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
        // 1. Retrieve current entry
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

        // 2. Perform LLM Merge
        var chatClient = _llmClientFactory.CreateChatClient();
        var userMessage = 
            $"# 既有知識條目內容：\n{currentEntry.Content}\n\n" +
            $"# 待融合的新文件內容：\n{newDocumentContent}";

        var mergedContent = await chatClient.CompleteAsync(MergeSystemPrompt, userMessage, ct);

        // 3. Archive current version to history
        await _versionControl.ArchiveAsync(currentEntry, ct);

        // 4. Update entry in Relational Database
        var nextVersion = currentEntry.Version + 1;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpdateEntryAsync(entryId, mergedContent, nextVersion, now, ct);

        // 5. Compute new vector and update Vector Database
        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var newVector = await embeddingClient.EmbedAsync(mergedContent, ct);
        await _lanceDbClient.UpsertEntryVectorAsync(entryId, currentEntry.Title, newVector, ct);

        return new KnowledgeEntry
        {
            EntryId = entryId,
            Title = currentEntry.Title,
            Content = mergedContent,
            Version = nextVersion,
            UpdatedAt = now
        };
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
        // 1. Retrieve the target version snapshot
        var snapshot = await _versionControl.GetVersionAsync(entryId, targetVersion, ct);
        if (snapshot == null)
        {
            throw new KeyNotFoundException($"Version {targetVersion} of entry {entryId} not found in history.");
        }

        // 2. Retrieve current entry
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

        // 3. Archive current version before rollback
        await _versionControl.ArchiveAsync(currentEntry, ct);

        // 4. Restore target content to entry in SQLite
        var nextVersion = currentEntry.Version + 1;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpdateEntryAsync(entryId, snapshot.ContentSnapshot, nextVersion, now, ct);

        // 5. Update LanceDB Vector
        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var restoredVector = await embeddingClient.EmbedAsync(snapshot.ContentSnapshot, ct);
        await _lanceDbClient.UpsertEntryVectorAsync(entryId, currentEntry.Title, restoredVector, ct);
    }
}
