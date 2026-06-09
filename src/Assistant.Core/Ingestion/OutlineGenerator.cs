using Assistant.Core.Config;
using Assistant.Core.LlmClient;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Assistant.Core.Ingestion;

public sealed class OutlineGenerator(ILlmClientFactory llmClientFactory, IConfigService configService) : IOutlineGenerator
{
    private readonly ILlmClientFactory _llmClientFactory = llmClientFactory;
    private readonly IConfigService _configService = configService;

    public async Task<OutlineResult> GenerateOutlineAsync(string documentContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            return new OutlineResult(string.Empty, string.Empty);
        }

        var settings = await _configService.LoadAsync(ct);
        var limit = settings.SummaryLimit > 0 ? settings.SummaryLimit : 200;

        var systemPrompt = 
            "你是一個高效率的知識庫管理專家。請閱讀原始文件內容，並輸出一個 JSON 格式的物件，包含以下屬性：\n" +
            "1. \"title\": 為該文件產生一個簡短（30字以內）、具代表性的繁體中文標題，不要有額外的說明或引號。\n" +
            $"2. \"summary\": 將文件內容提煉並壓縮成一份不超過 {limit} 字的結構化繁體中文大綱，需去除無關噪音，包含核心主旨與關鍵要點。\n\n" +
            "請嚴格只輸出一個合法的 JSON 物件，不要包含任何自我介紹、額外解釋或 Markdown 語法標記（如 ```json）。例如：\n" +
            "{\n  \"title\": \"標題內容\",\n  \"summary\": \"大綱內容\"\n}";

        var chatClient = _llmClientFactory.CreateChatClient();
        var response = await chatClient.CompleteAsync(systemPrompt, documentContent, ct);

        return ParseOutlineResult(response);
    }

    private static OutlineResult ParseOutlineResult(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return new OutlineResult(string.Empty, string.Empty);
        }

        var text = response.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(7);
        }
        else if (text.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(3);
        }
        if (text.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 3);
        }
        text = text.Trim();

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var title = root.GetProperty("title").GetString() ?? string.Empty;
            var summary = root.GetProperty("summary").GetString() ?? string.Empty;
            return new OutlineResult(title.Trim(), summary.Trim());
        }
        catch
        {
            var titleMatch = Regex.Match(text, @"""title""\s*:\s*""([^""]+)""");
            var summaryMatch = Regex.Match(text, @"""summary""\s*:\s*""([^""]+)""");

            var title = titleMatch.Success ? titleMatch.Groups[1].Value : "未命名文件";
            var summary = summaryMatch.Success ? summaryMatch.Groups[1].Value : text;

            return new OutlineResult(title.Trim(), summary.Trim());
        }
    }
}
