using Assistant.Core.Clustering;
using Assistant.Core.KnowledgeBase;
using Assistant.Core.LlmClient;
using Assistant.Core.Search;
using Assistant.Core.Storage;
using System.Text;

namespace Assistant.Core.Ingestion;

public sealed class IngestionService : IIngestionService
{
    private readonly IRelationalRepository _relationalRepository;
    private readonly IOutlineGenerator _outlineGenerator;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly ILanceDbClient _lanceDbClient;
    private readonly IVectorSearchEngine _vectorSearchEngine;
    private readonly IRoutingDecision _routingDecision;
    private readonly IKnowledgeEntryService _knowledgeEntryService;
    private readonly IHdbscanEngine _hdbscanEngine;

    private const string TitleGenPrompt =
        "你是一個知識庫管理助手。請根據以下文件的結構化大綱，為其命名一個簡短、精確且有代表性的標題（不超過 30 字，使用繁體中文）。不要有任何額外的字眼、前綴、解釋或標點符號，請直接輸出標題文字本身。";

    private const string MultiDocumentMergePrompt =
        "你是一個高級知識庫融合專家。請將以下多份相關文件的內容與大綱整合成一篇結構完整、條理清晰且高可讀性的 Markdown 主題文章。\n" +
        "請遵循以下規則：\n" +
        "1. 去除重複資訊與雜訊，有機地融合所有互補與更新的資訊細節。\n" +
        "2. 保持條理清晰，段落分明，結構完整，使用繁體中文。\n" +
        "3. 輸出必須是完整的 Markdown 內容，不得有任何簡略標記（如「同前文」、「此處省略」）或省略號。\n" +
        "4. 不要包含任何自我介紹或解釋，請直接輸出整整合後的 Markdown 文章。";

    private const string MultiDocumentTitleGenPrompt =
        "你是一個知識庫管理助手。請根據以下多份文件的大綱，為其命名一個能概括所有相關內容的簡短、精確且有代表性的標題（不超過 30 字，使用繁體中文）。不要有任何額外的字眼、前綴、解釋或標點符號，請直接輸出標題文字本身。";

    public IngestionService(
        IRelationalRepository relationalRepository,
        IOutlineGenerator outlineGenerator,
        ILlmClientFactory llmClientFactory,
        ILanceDbClient lanceDbClient,
        IVectorSearchEngine vectorSearchEngine,
        IRoutingDecision routingDecision,
        IKnowledgeEntryService knowledgeEntryService,
        IHdbscanEngine hdbscanEngine)
    {
        _relationalRepository = relationalRepository;
        _outlineGenerator = outlineGenerator;
        _llmClientFactory = llmClientFactory;
        _lanceDbClient = lanceDbClient;
        _vectorSearchEngine = vectorSearchEngine;
        _routingDecision = routingDecision;
        _knowledgeEntryService = knowledgeEntryService;
        _hdbscanEngine = hdbscanEngine;
    }

    public async Task IngestAsync(RawDocument document, CancellationToken ct = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        // 1. Save Raw Document
        await _relationalRepository.InsertDocumentAsync(
            document.DocumentId, document.Content, document.Source, document.CreatedAt, ct);

        // 2. Generate Outline (Summary)
        var summary = await _outlineGenerator.GenerateOutlineAsync(document.Content, ct);
        var outlineId = Guid.NewGuid();

        // 3. Save Outline to Relational DB
        await _relationalRepository.InsertOutlineAsync(outlineId, document.DocumentId, summary, ct);

        // 4. Calculate Outline Embedding
        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var outlineVector = await embeddingClient.EmbedAsync(summary, ct);

        // 5. Upsert Outline Vector to LanceDB
        await _lanceDbClient.UpsertOutlineVectorAsync(outlineId, document.Source, outlineVector, ct);

        // 6. Perform Vector Search on existing knowledge entries
        var searchResults = await _vectorSearchEngine.SearchKnowledgeEntriesAsync(outlineVector, topK: 5, ct);

        // 7. Decide routing (Create New or Merge)
        var decision = _routingDecision.Decide(searchResults);

        if (decision.Action == RoutingAction.Merge && decision.BestMatch != null)
        {
            // Case B: Merge into existing entry
            await _knowledgeEntryService.MergeAsync(decision.BestMatch.EntryId, document.Content, ct);
        }
        else
        {
            // Case A: Create new independent entry
            var chatClient = _llmClientFactory.CreateChatClient();
            var generatedTitle = await chatClient.CompleteAsync(TitleGenPrompt, summary, ct);
            generatedTitle = generatedTitle.Trim('\"', '\'', ' ', '\r', '\n');

            await _knowledgeEntryService.CreateAsync(generatedTitle, document.Content, ct);
        }
    }

    public async Task IngestBatchAsync(IEnumerable<RawDocument> documents, CancellationToken ct = default)
    {
        if (documents == null) return;
        var docList = documents.ToList();
        if (docList.Count == 0) return;

        var outlines = new List<string>();
        var outlineIds = new List<Guid>();

        // 1. Process documents, generate outlines, save to relational database
        foreach (var doc in docList)
        {
            await _relationalRepository.InsertDocumentAsync(
                doc.DocumentId, doc.Content, doc.Source, doc.CreatedAt, ct);

            var summary = await _outlineGenerator.GenerateOutlineAsync(doc.Content, ct);
            var outlineId = Guid.NewGuid();

            await _relationalRepository.InsertOutlineAsync(outlineId, doc.DocumentId, summary, ct);

            outlines.Add(summary);
            outlineIds.Add(outlineId);
        }

        // 2. Batch calculate embeddings
        var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
        var vectors = await embeddingClient.EmbedBatchAsync(outlines, ct);

        // 3. Save outline vectors to LanceDB
        for (int i = 0; i < docList.Count; i++)
        {
            await _lanceDbClient.UpsertOutlineVectorAsync(
                outlineIds[i], docList[i].Source, vectors[i], ct);
        }

        // 4. Run Global HDBSCAN Clustering
        var clusterLabels = await _hdbscanEngine.ClusterAsync(vectors, ct);

        // Group documents and summaries by their cluster label
        var clusters = new Dictionary<int, List<(RawDocument Doc, string Summary)>>();
        for (int i = 0; i < docList.Count; i++)
        {
            int label = clusterLabels[i];
            if (!clusters.TryGetValue(label, out List<(RawDocument Doc, string Summary)>? value))
            {
                value = [];
                clusters[label] = value;
            }
            value.Add((docList[i], outlines[i]));
        }

        var chatClient = _llmClientFactory.CreateChatClient();

        // 5. Handle clusters
        foreach (var kvp in clusters)
        {
            int label = kvp.Key;
            var groupItems = kvp.Value;

            if (label == -1)
            {
                // Noise items: each is created as an independent new entry
                foreach (var item in groupItems)
                {
                    var title = await chatClient.CompleteAsync(TitleGenPrompt, item.Summary, ct);
                    title = title.Trim('\"', '\'', ' ', '\r', '\n');

                    await _knowledgeEntryService.CreateAsync(title, item.Doc.Content, ct);
                }
            }
            else
            {
                // Solid cluster: merge all documents in the cluster into one entry
                var mergedContentBuilder = new StringBuilder();
                var combinedSummariesBuilder = new StringBuilder();

                for (int i = 0; i < groupItems.Count; i++)
                {
                    var item = groupItems[i];
                    mergedContentBuilder.AppendLine($"--- 文件 {i + 1} ({item.Doc.Source}) ---");
                    mergedContentBuilder.AppendLine(item.Doc.Content);
                    mergedContentBuilder.AppendLine();

                    combinedSummariesBuilder.AppendLine($"- 大綱 {i + 1}: {item.Summary}");
                }

                // Generate cluster summary title
                var clusterTitle = await chatClient.CompleteAsync(
                    MultiDocumentTitleGenPrompt, combinedSummariesBuilder.ToString(), ct);
                clusterTitle = clusterTitle.Trim('\"', '\'', ' ', '\r', '\n');

                // Perform cluster merge
                var mergedContent = await chatClient.CompleteAsync(
                    MultiDocumentMergePrompt, mergedContentBuilder.ToString(), ct);

                await _knowledgeEntryService.CreateAsync(clusterTitle, mergedContent, ct);
            }
        }
    }
}
