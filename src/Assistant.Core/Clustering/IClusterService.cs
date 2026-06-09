namespace Assistant.Core.Clustering;

public record ClusterEntryDto(Guid EntryId, string Title, int Version, DateTimeOffset UpdatedAt);
public record ClusterDetailDto(Guid ClusterId, string Name, IReadOnlyList<ClusterEntryDto> Entries);

public interface IClusterService
{
    /// <summary>
    /// 重新對所有知識條目進行分群並更新資料庫
    /// </summary>
    Task ReclusterAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得所有分群清單（包含每個分群下的條目清單）
    /// </summary>
    Task<IReadOnlyList<ClusterDetailDto>> GetClustersAsync(CancellationToken ct = default);
}
