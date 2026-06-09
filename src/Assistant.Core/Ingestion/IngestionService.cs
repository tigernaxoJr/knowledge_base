using Assistant.Core.Clustering;
using Assistant.Core.KnowledgeBase;
using Assistant.Core.LlmClient;
using Assistant.Core.Prompts;
using Assistant.Core.Search;
using Assistant.Core.Storage;
using System.Text;

namespace Assistant.Core.Ingestion;

public sealed class IngestionService(
    IRelationalRepository relationalRepository,
    IOutlineGenerator outlineGenerator,
    ILlmClientFactory llmClientFactory,
    ILanceDbClient lanceDbClient,
    IVectorSearchEngine vectorSearchEngine,
    IRoutingDecision routingDecision,
    IKnowledgeEntryService knowledgeEntryService,
    IHdbscanEngine hdbscanEngine,
    IPromptProvider prompts,
    IClusterService clusterService) : IIngestionService
{
    private readonly IRelationalRepository _relationalRepository = relationalRepository;
    private readonly IOutlineGenerator _outlineGenerator = outlineGenerator;
    private readonly ILlmClientFactory _llmClientFactory = llmClientFactory;
    private readonly ILanceDbClient _lanceDbClient = lanceDbClient;
    private readonly IVectorSearchEngine _vectorSearchEngine = vectorSearchEngine;
    private readonly IRoutingDecision _routingDecision = routingDecision;
    private readonly IKnowledgeEntryService _knowledgeEntryService = knowledgeEntryService;
    private readonly IHdbscanEngine _hdbscanEngine = hdbscanEngine;
    private readonly IPromptProvider _prompts = prompts;
    private readonly IClusterService _clusterService = clusterService;

    public async Task IngestAsync(RawDocument document, CancellationToken ct = default)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var operationId = await _relationalRepository.StartOperationAsync(
            OperationKind.Ingestion, document.DocumentId, document.Source, ct);

        try
        {
            await _relationalRepository.InsertDocumentAsync(
                document.DocumentId, document.Content, document.Source, document.CreatedAt, ct);

            var summary = await _outlineGenerator.GenerateOutlineAsync(document.Content, ct);
            var outlineId = Guid.NewGuid();

            await _relationalRepository.InsertOutlineAsync(outlineId, document.DocumentId, summary, ct);

            var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
            var outlineVector = await embeddingClient.EmbedAsync(summary, ct);

            await _lanceDbClient.UpsertOutlineVectorAsync(outlineId, document.Source, outlineVector, ct);

            var searchResults = await _vectorSearchEngine.SearchKnowledgeEntriesAsync(outlineVector, topK: 5, ct);
            var decision = _routingDecision.Decide(searchResults);

            if (decision.Action == RoutingAction.Merge && decision.BestMatch != null)
            {
                await _knowledgeEntryService.MergeAsync(decision.BestMatch.EntryId, document.Content, ct: ct);
            }
            else
            {
                var chatClient = _llmClientFactory.CreateChatClient();
                var generatedTitle = await chatClient.CompleteAsync(_prompts.TitleGeneration, summary, ct);
                generatedTitle = generatedTitle.Trim('\"', '\'', ' ', '\r', '\n');

                await _knowledgeEntryService.CreateAsync(generatedTitle, document.Content, ct: ct);
            }

            await _relationalRepository.CompleteOperationAsync(operationId, ct);
        }
        catch (Exception ex)
        {
            await _relationalRepository.FailOperationAsync(operationId, ex.Message, ct);
            throw;
        }
    }

    public async Task IngestBatchAsync(IEnumerable<RawDocument> documents, CancellationToken ct = default)
    {
        if (documents == null) return;
        var docList = documents.ToList();
        if (docList.Count == 0) return;

        var operationId = await _relationalRepository.StartOperationAsync(
            OperationKind.BatchIngestion, null, $"batch:{docList.Count}", ct);

        try
        {
            var outlines = new List<string>();
            var outlineIds = new List<Guid>();

            // 1. Concurrent outline generation
            var outlineTasks = docList.Select(async doc =>
            {
                var summary = await _outlineGenerator.GenerateOutlineAsync(doc.Content, ct);
                var outlineId = Guid.NewGuid();
                return (Doc: doc, Summary: summary, OutlineId: outlineId);
            }).ToList();

            var outlineResults = await Task.WhenAll(outlineTasks);

            // 2. Sequential database writes
            foreach (var res in outlineResults)
            {
                await _relationalRepository.InsertDocumentAsync(
                    res.Doc.DocumentId, res.Doc.Content, res.Doc.Source, res.Doc.CreatedAt, ct);

                await _relationalRepository.InsertOutlineAsync(res.OutlineId, res.Doc.DocumentId, res.Summary, ct);

                outlines.Add(res.Summary);
                outlineIds.Add(res.OutlineId);
            }

            var embeddingClient = _llmClientFactory.CreateEmbeddingClient();
            var vectors = await embeddingClient.EmbedBatchAsync(outlines, ct);

            for (int i = 0; i < docList.Count; i++)
            {
                await _lanceDbClient.UpsertOutlineVectorAsync(
                    outlineIds[i], docList[i].Source, vectors[i], ct);
            }

            var clusterLabels = await _hdbscanEngine.ClusterAsync(vectors, ct);
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
            var clusterActions = new List<Func<Task>>();
            var llmTasks = new List<Task>();

            foreach (var kvp in clusters)
            {
                int label = kvp.Key;
                var groupItems = kvp.Value;

                if (label == -1)
                {
                    foreach (var item in groupItems)
                    {
                        var currentItem = item;
                        var llmTask = Task.Run(async () =>
                        {
                            var title = await chatClient.CompleteAsync(_prompts.TitleGeneration, currentItem.Summary, ct);
                            title = title.Trim('\"', '\'', ' ', '\r', '\n');

                            lock (clusterActions)
                            {
                                clusterActions.Add(() => _knowledgeEntryService.CreateAsync(
                                    title, currentItem.Doc.Content, triggerRecluster: false, ct: ct));
                            }
                        }, ct);
                        llmTasks.Add(llmTask);
                    }
                }
                else
                {
                    var mergedContentBuilder = new StringBuilder();
                    var combinedSummariesBuilder = new StringBuilder();

                    for (int i = 0; i < groupItems.Count; i++)
                    {
                        var item = groupItems[i];
                        mergedContentBuilder.AppendLine($"--- Document {i + 1} ({item.Doc.Source}) ---");
                        mergedContentBuilder.AppendLine(item.Doc.Content);
                        mergedContentBuilder.AppendLine();

                        combinedSummariesBuilder.AppendLine($"- Outline {i + 1}: {item.Summary}");
                    }

                    var combinedSummaries = combinedSummariesBuilder.ToString();
                    var mergedContentRaw = mergedContentBuilder.ToString();

                    var llmTask = Task.Run(async () =>
                    {
                        var titleTask = chatClient.CompleteAsync(_prompts.MultiDocumentTitleGeneration, combinedSummaries, ct);
                        var mergeTask = chatClient.CompleteAsync(_prompts.MultiDocumentMerge, mergedContentRaw, ct);

                        await Task.WhenAll(titleTask, mergeTask);

                        var title = (await titleTask).Trim('\"', '\'', ' ', '\r', '\n');
                        var mergedContent = await mergeTask;

                        lock (clusterActions)
                        {
                            clusterActions.Add(() => _knowledgeEntryService.CreateAsync(
                                title, mergedContent, triggerRecluster: false, ct: ct));
                        }
                    }, ct);
                    llmTasks.Add(llmTask);
                }
            }

            await Task.WhenAll(llmTasks);

            // Execute DB writes sequentially to avoid SQLite locking
            foreach (var action in clusterActions)
            {
                await action();
            }

            await _clusterService.ReclusterAsync(ct);
            await _relationalRepository.CompleteOperationAsync(operationId, ct);
        }
        catch (Exception ex)
        {
            await _relationalRepository.FailOperationAsync(operationId, ex.Message, ct);
            throw;
        }
    }
}
