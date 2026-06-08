namespace Assistant.Core.Clustering;

/// <summary>HDBSCAN 聚群引擎介面</summary>
public interface IHdbscanEngine
{
    /// <summary>
    /// 冷啟動：對所有大綱向量執行 HDBSCAN 聚群，
    /// 回傳每個向量所屬的 Cluster ID（-1 表示噪音點）。
    /// </summary>
    /// <param name="vectors">大綱 Embedding 向量集合</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>與 <paramref name="vectors"/> 等長的 Cluster ID 陣列</returns>
    Task<int[]> ClusterAsync(IReadOnlyList<float[]> vectors, CancellationToken ct = default);

    /// <summary>
    /// 定期維護：僅對新增大綱向量進行局部聚群，
    /// 判斷是否有新 Cluster 自動浮現。
    /// </summary>
    /// <param name="newVectors">本次維護週期新增的大綱向量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新發現的 Cluster 索引集合（空集合表示無新 Cluster）</returns>
    Task<IReadOnlyList<int[]>> IncrementalClusterAsync(
        IReadOnlyList<float[]> newVectors, CancellationToken ct = default);
}
