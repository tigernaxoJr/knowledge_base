using Assistant.Core.Clustering;
using Assistant.Core.Config;
using Xunit;

namespace Assistant.Core.Tests;

public class ClusteringTests
{
    private class MockConfigService(int minPts) : IConfigService
    {
        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new AppSettings
            {
                ClusteringConfig = new ClusteringConfig { MinPts = minPts, Eps = 0.25 }
            });
        }

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;

        public Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(
            string endpoint, string apiKey, string modelName, CancellationToken ct = default)
        {
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    [Fact]
    public async Task ClusterAsync_ShouldGroupSimilarVectorsAndIdentifyNoise()
    {
        var engine = new HdbscanEngine(new MockConfigService(2));

        // High dimensional simulated embeddings (normalized 3D vectors)
        var vectors = new List<float[]>
        {
            new float[] { 1.0f, 0.0f, 0.0f },      // Theme A - Doc 1
            new float[] { 0.98f, 0.02f, 0.0f },    // Theme A - Doc 2
            new float[] { 0.95f, -0.05f, 0.01f },  // Theme A - Doc 3
            
            new float[] { 0.0f, 1.0f, 0.0f },      // Theme B - Doc 1
            new float[] { 0.01f, 0.99f, -0.02f },  // Theme B - Doc 2
            
            new float[] { 0.0f, 0.0f, 1.0f }       // Noise - Outlier Doc
        };

        var labels = await engine.ClusterAsync(vectors);

        Assert.Equal(6, labels.Length);
        
        // Assert Theme A has the same valid cluster ID
        Assert.True(labels[0] >= 0);
        Assert.Equal(labels[0], labels[1]);
        Assert.Equal(labels[0], labels[2]);

        // Assert Theme B has the same valid cluster ID, but different from Theme A
        Assert.True(labels[3] >= 0);
        Assert.Equal(labels[3], labels[4]);
        Assert.NotEqual(labels[0], labels[3]);

        // Assert the isolated outlier vector is classified as noise (-1)
        Assert.Equal(-1, labels[5]);
    }

    [Fact]
    public async Task IncrementalClusterAsync_ShouldReturnClusterIndices()
    {
        var engine = new HdbscanEngine(new MockConfigService(2));

        var newVectors = new List<float[]>
        {
            new float[] { 1.0f, 0.0f, 0.0f },
            new float[] { 0.0f, 1.0f, 0.0f },
            new float[] { 0.99f, 0.01f, 0.0f },
            new float[] { 0.0f, 0.0f, 1.0f }
        };

        // Indices:
        // 0 and 2 are highly similar (Theme A cluster)
        // 1 and 3 are outliers (different dimensions)

        var clusters = await engine.IncrementalClusterAsync(newVectors);

        Assert.Single(clusters); // Expect exactly 1 cluster discovered
        var firstCluster = clusters[0];
        
        Assert.Contains(0, firstCluster);
        Assert.Contains(2, firstCluster);
        Assert.DoesNotContain(1, firstCluster);
        Assert.DoesNotContain(3, firstCluster);
    }
}
