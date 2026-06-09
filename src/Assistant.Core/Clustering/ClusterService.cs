using System.Globalization;
using Assistant.Core.Clustering;
using Assistant.Core.Storage;
using Assistant.Core.LlmClient;

namespace Assistant.Core.Clustering;

public sealed class ClusterService(
    IRelationalRepository relationalRepository,
    ILanceDbClient lanceDbClient,
    IHdbscanEngine hdbscanEngine,
    ILlmClientFactory llmClientFactory) : IClusterService
{
    private readonly IRelationalRepository _relationalRepository = relationalRepository;
    private readonly ILanceDbClient _lanceDbClient = lanceDbClient;
    private readonly IHdbscanEngine _hdbscanEngine = hdbscanEngine;
    private readonly ILlmClientFactory _llmClientFactory = llmClientFactory;

    public async Task ReclusterAsync(CancellationToken ct = default)
    {
        // 1. Get all entries and all vectors
        var entries = await _relationalRepository.GetAllEntriesAsync(ct);
        if (entries.Count == 0)
        {
            await _relationalRepository.ClearClustersAsync(ct);
            return;
        }

        var entryVectors = await _lanceDbClient.GetAllEntryVectorsAsync(ct);
        var vectorMap = entryVectors.ToDictionary(v => v.EntryId, v => v.Vector);

        // Filter out entries that have no vectors
        var validEntries = entries.Where(e => vectorMap.ContainsKey(e.EntryId)).ToList();
        if (validEntries.Count == 0)
        {
            await _relationalRepository.ClearClustersAsync(ct);
            return;
        }

        // 2. Run HDBSCAN
        var vectors = validEntries.Select(e => vectorMap[e.EntryId]).ToList();
        var labels = await _hdbscanEngine.ClusterAsync(vectors, ct);

        // Group entry IDs by new cluster labels (excluding noise -1)
        var newClusterGroups = new Dictionary<int, List<Guid>>();
        for (int i = 0; i < validEntries.Count; i++)
        {
            int label = labels[i];
            if (label == -1) continue; // Skip noise

            if (!newClusterGroups.TryGetValue(label, out List<Guid>? list))
            {
                list = [];
                newClusterGroups[label] = list;
            }
            list.Add(validEntries[i].EntryId);
        }

        // 3. Load existing clusters and their entry mappings to perform Jaccard matching
        var existingClusters = await _relationalRepository.GetClustersAsync(ct);
        var entriesWithClusters = await _relationalRepository.GetEntriesWithClusterAsync(ct);

        // existingClusterToEntries: ClusterId -> Set of EntryId
        var existingClusterToEntries = entriesWithClusters
            .Where(e => e.ClusterId.HasValue)
            .GroupBy(e => e.ClusterId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.EntryId).ToHashSet());

        var existingClusterNames = existingClusters.ToDictionary(c => c.ClusterId, c => c.Name);

        // Match new clusters to existing clusters
        var finalClusterAssignments = new Dictionary<Guid, (string Name, List<Guid> EntryIds)>();
        var matchedExistingClusters = new HashSet<Guid>();
        var chatClient = _llmClientFactory.CreateChatClient();

        foreach (var newGroup in newClusterGroups.Values)
        {
            var newSet = newGroup.ToHashSet();
            Guid? matchedClusterId = null;
            double bestSimilarity = 0.0;

            foreach (var existingKvp in existingClusterToEntries)
            {
                var existingClusterId = existingKvp.Key;
                if (matchedExistingClusters.Contains(existingClusterId)) continue;

                var existingSet = existingKvp.Value;
                
                // Calculate Jaccard similarity
                int intersectionSize = newSet.Intersect(existingSet).Count();
                int unionSize = newSet.Union(existingSet).Count();
                double similarity = unionSize > 0 ? (double)intersectionSize / unionSize : 0.0;

                if (similarity >= 0.4 && similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    matchedClusterId = existingClusterId;
                }
            }

            Guid clusterId;
            string clusterName;

            if (matchedClusterId.HasValue)
            {
                clusterId = matchedClusterId.Value;
                matchedExistingClusters.Add(clusterId);

                // Check if member composition has changed
                var existingMemberSet = existingClusterToEntries[clusterId];
                bool membersChanged = !newSet.SetEquals(existingMemberSet);

                if (membersChanged)
                {
                    // Members changed → force LLM to regenerate cluster name
                    var groupEntries = validEntries.Where(e => newSet.Contains(e.EntryId)).ToList();
                    clusterName = await GenerateClusterNameAsync(groupEntries, chatClient, ct);
                }
                else
                {
                    // Members identical → keep existing name
                    clusterName = existingClusterNames[clusterId];
                }
            }
            else
            {
                clusterId = Guid.NewGuid();
                var groupEntries = validEntries.Where(e => newSet.Contains(e.EntryId)).ToList();
                clusterName = await GenerateClusterNameAsync(groupEntries, chatClient, ct);
            }

            finalClusterAssignments[clusterId] = (clusterName, newGroup);
        }

        // 4. Update the database
        await _relationalRepository.ClearClustersAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var clusterKvp in finalClusterAssignments)
        {
            var clusterId = clusterKvp.Key;
            var (name, entryIds) = clusterKvp.Value;

            await _relationalRepository.InsertClusterAsync(clusterId, name, now, ct);
            foreach (var entryId in entryIds)
            {
                await _relationalRepository.UpdateEntryClusterAsync(entryId, clusterId, ct);
            }
        }

        // Any entry in validEntries that is not in any new group should have its cluster_id set to null
        var allClusteredEntryIds = finalClusterAssignments.Values.SelectMany(v => v.EntryIds).ToHashSet();
        foreach (var entry in validEntries)
        {
            if (!allClusteredEntryIds.Contains(entry.EntryId))
            {
                await _relationalRepository.UpdateEntryClusterAsync(entry.EntryId, null, ct);
            }
        }
    }

    public async Task<IReadOnlyList<ClusterDetailDto>> GetClustersAsync(CancellationToken ct = default)
    {
        var clusters = await _relationalRepository.GetClustersAsync(ct);
        var entriesWithClusters = await _relationalRepository.GetEntriesWithClusterAsync(ct);

        // Group entries by cluster_id (using Guid.Empty for nulls)
        var clusterEntriesMap = entriesWithClusters
            .GroupBy(e => e.ClusterId ?? Guid.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => new ClusterEntryDto(e.EntryId, e.Title, e.Version, e.UpdatedAt)).ToList()
            );

        var results = new List<ClusterDetailDto>();

        // 1. Add persistent clusters
        foreach (var cluster in clusters)
        {
            var entries = clusterEntriesMap.TryGetValue(cluster.ClusterId, out var list) ? list : new List<ClusterEntryDto>();
            results.Add(new ClusterDetailDto(cluster.ClusterId, cluster.Name, entries));
        }

        // 2. Add "未分類" virtual cluster for noise entries (cluster_id IS NULL)
        if (clusterEntriesMap.TryGetValue(Guid.Empty, out var uncategorizedEntries) && uncategorizedEntries.Count > 0)
        {
            results.Add(new ClusterDetailDto(Guid.Empty, "未分類", uncategorizedEntries));
        }

        return results;
    }

    /// <summary>
    /// 使用 LLM 為分群產生代表性名稱。
    /// 當成員涵蓋多個不同領域時，prompt 引導 LLM 使用更通用的上位詞。
    /// </summary>
    private static async Task<string> GenerateClusterNameAsync(
        List<(Guid EntryId, string Title, string Content, int Version, DateTimeOffset UpdatedAt)> entries,
        IChatClient chatClient,
        CancellationToken ct)
    {
        var titles = string.Join("\n", entries.Select(e => $"- {e.Title}"));

        var prompt =
            "你是一個知識庫管理專家。請針對以下這一組相關知識條目的標題，" +
            "產生一個簡短（10個字以內）、具代表性的分類名稱。" +
            "如果條目涵蓋多個不同領域或主題，請使用更通用的上位概念作為名稱" +
            "（例如：當同時包含資通安全法與醫療法時，應命名為「台灣法規」而非「資通安全法規」）。" +
            "請直接輸出繁體中文名稱，不要有額外的說明或引號：\n\n" + titles;

        var name = await chatClient.CompleteAsync(string.Empty, prompt, ct);
        name = name.Trim('"', '\'', ' ', '\r', '\n');

        return string.IsNullOrWhiteSpace(name) ? "主題群組" : name;
    }
}
