namespace Assistant.Core.Ingestion;

public sealed record OutlineResult(string Title, string Summary);

/// <summary>大綱與標題生成器介面：呼叫 LLM 將原始文件壓縮為大綱與標題</summary>
public interface IOutlineGenerator
{
    /// <summary>
    /// 將原始文件內容透過 LLM 生成繁體中文標題與結構化大綱。
    /// </summary>
    /// <param name="documentContent">原始文件全文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含標題與大綱的結果</returns>
    Task<OutlineResult> GenerateOutlineAsync(string documentContent, CancellationToken ct = default);
}
