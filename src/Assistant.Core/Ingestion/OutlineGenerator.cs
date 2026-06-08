using Assistant.Core.LlmClient;

namespace Assistant.Core.Ingestion;

public sealed class OutlineGenerator : IOutlineGenerator
{
    private readonly ILlmClientFactory _llmClientFactory;
    private const string SystemPrompt = 
        "你是一個高效率的知識庫管理助手。你的任務是閱讀原始文件內容，將其提煉並壓縮成一份不超過 400 字的結構化大綱。\n" +
        "請遵循以下規則：\n" +
        "1. 必須去噪音（去除無關的語氣詞、格式標記或重複性描述）。\n" +
        "2. 大綱必須包含核心主旨、關鍵要點及條理化結論，使讀者能藉由大綱快速理解原始文件細節。\n" +
        "3. 字數嚴格限制在 400 字以內，使用繁體中文。\n" +
        "4. 不要包含任何自我介紹或多餘解釋，請直接輸出大綱內容。";

    public OutlineGenerator(ILlmClientFactory llmClientFactory)
    {
        _llmClientFactory = llmClientFactory;
    }

    public async Task<string> GenerateOutlineAsync(string documentContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            return string.Empty;
        }

        var chatClient = _llmClientFactory.CreateChatClient();
        return await chatClient.CompleteAsync(SystemPrompt, documentContent, ct);
    }
}
