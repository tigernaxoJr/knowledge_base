namespace Assistant.Core.Ingestion;

/// <summary>大綱生成器介面：呼叫 LLM 將原始文件壓縮為 400 字摘要</summary>
public interface IOutlineGenerator
{
    /// <summary>
    /// 將原始文件內容透過 LLM 生成不超過 400 字的結構化大綱。
    /// </summary>
    /// <param name="documentContent">原始文件全文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>400 字大綱文字</returns>
    Task<string> GenerateOutlineAsync(string documentContent, CancellationToken ct = default);
}
