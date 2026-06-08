namespace Assistant.Core.LlmClient;

/// <summary>LLM Chat 客戶端介面（用於大綱生成、知識條目 Merge）</summary>
public interface IChatClient
{
    /// <summary>
    /// 呼叫 LLM Chat API 取得單輪回覆。
    /// </summary>
    /// <param name="systemPrompt">系統提示詞</param>
    /// <param name="userMessage">使用者訊息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>模型回覆文字</returns>
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}

/// <summary>Embedding 客戶端介面（用於大綱與知識條目的向量計算）</summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// 將文字轉換為 Embedding 向量。
    /// </summary>
    /// <param name="text">輸入文字</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>float 向量陣列</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>批次向量化（減少 API 呼叫次數）</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>
/// LLM 客戶端工廠介面：依設定動態建立 Chat / Embedding 客戶端。
/// 當設定（Endpoint / ApiKey）變更時重新建立實例，無需重啟程式。
/// </summary>
public interface ILlmClientFactory
{
    IChatClient CreateChatClient();
    IEmbeddingClient CreateEmbeddingClient();

    /// <summary>設定變更後呼叫，使後續 Create* 方法回傳使用新設定的實例</summary>
    void Reload();
}
