using Assistant.Core.Config;

namespace Assistant.Core.Clustering;

public sealed class HdbscanEngine(IConfigService? configService = null) : IHdbscanEngine
{
    private readonly IConfigService? _configService = configService;
    private const float DefaultEps = 0.25f; // Loosened from 0.18f to 0.25f to be more inclusive by default
    private const int DefaultMinPts = 2;

    public async Task<int[]> ClusterAsync(IReadOnlyList<float[]> vectors, CancellationToken ct = default)
    {
        if (vectors == null || vectors.Count == 0)
        {
            return Array.Empty<int>();
        }

        float eps = DefaultEps;
        int minPts = DefaultMinPts;

        if (_configService != null)
        {
            var settings = await _configService.LoadAsync(ct);
            eps = (float)settings.ClusteringConfig.Eps;
            minPts = settings.ClusteringConfig.MinPts;
        }

        int n = vectors.Count;
        int[] labels = new int[n];
        Array.Fill(labels, -2); // -2 represents UNCLASSIFIED

        int clusterId = 0;

        for (int i = 0; i < n; i++)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (labels[i] != -2)
            {
                continue;
            }

            var neighbors = GetNeighbors(i, vectors, eps);
            if (neighbors.Count < minPts)
            {
                labels[i] = -1; // Noise
            }
            else
            {
                ExpandCluster(i, neighbors, labels, clusterId, vectors, eps, minPts, ct);
                clusterId++;
            }
        }

        // Convert any remaining unclassified points to noise
        for (int i = 0; i < n; i++)
        {
            if (labels[i] == -2)
            {
                labels[i] = -1;
            }
        }

        return labels;
    }

    public async Task<IReadOnlyList<int[]>> IncrementalClusterAsync(
        IReadOnlyList<float[]> newVectors, CancellationToken ct = default)
    {
        if (newVectors == null || newVectors.Count == 0)
        {
            return Array.Empty<int[]>();
        }

        // Run DBSCAN on the new vectors
        int[] labels = await ClusterAsync(newVectors, ct);

        // Group indices by cluster ID (excluding noise -1)
        var groups = labels
            .Select((label, index) => new { label, index })
            .Where(x => x.label >= 0)
            .GroupBy(x => x.label)
            .Select(g => g.Select(x => x.index).ToArray())
            .ToList();

        return groups;
    }

    private static List<int> GetNeighbors(int index, IReadOnlyList<float[]> vectors, float eps)
    {
        var neighbors = new List<int>();
        var point = vectors[index];

        for (int i = 0; i < vectors.Count; i++)
        {
            float dist = CosineDistance(point, vectors[i]);
            if (dist <= eps)
            {
                neighbors.Add(i);
            }
        }

        return neighbors;
    }

    private static void ExpandCluster(
        int index, List<int> neighbors, int[] labels, int clusterId,
        IReadOnlyList<float[]> vectors, float eps, int minPts, CancellationToken ct)
    {
        labels[index] = clusterId;

        for (int i = 0; i < neighbors.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            int neighborIndex = neighbors[i];

            if (labels[neighborIndex] == -1) // Noise point becomes boundary point of cluster
            {
                labels[neighborIndex] = clusterId;
            }
            else if (labels[neighborIndex] == -2) // Unclassified
            {
                labels[neighborIndex] = clusterId;

                var nextNeighbors = GetNeighbors(neighborIndex, vectors, eps);
                if (nextNeighbors.Count >= minPts)
                {
                    // Add new neighbors to search list if they are not already there
                    foreach (var nextNeighbor in nextNeighbors)
                    {
                        if (!neighbors.Contains(nextNeighbor))
                        {
                            neighbors.Add(nextNeighbor);
                        }
                    }
                }
            }
        }
    }

    private static float CosineDistance(float[] vecA, float[] vecB)
    {
        if (vecA.Length != vecB.Length || vecA.Length == 0)
        {
            return 1.0f;
        }

        float dotProduct = 0.0f;
        float normA = 0.0f;
        float normB = 0.0f;

        for (int i = 0; i < vecA.Length; i++)
        {
            dotProduct += vecA[i] * vecB[i];
            normA += vecA[i] * vecA[i];
            normB += vecB[i] * vecB[i];
        }

        if (normA == 0.0f || normB == 0.0f)
        {
            return 1.0f;
        }

        float similarity = dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
        return 1.0f - similarity;
    }
}
