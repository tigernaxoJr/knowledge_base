using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Assistant.Core.Config;

namespace Assistant.Core.LlmClient;

public sealed class EmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingConfig _config;

    public EmbeddingClient(HttpClient httpClient, EmbeddingConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await EmbedBatchAsync([text], ct);
        return result[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts == null || texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var endpoint = _config.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("向量模型 API 端點 (Endpoint) 未配置或不是有效的絕對 URL。請先至首頁右上角的「設定」頁面配置向量模型端點與 API 金鑰！");
        }
        var requestUrl = endpoint.TrimEnd('/') + "/embeddings";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        // 避免本地或部分輕量向量模型伺服器的 512-token 實體批次大小限制（如 Ollama/llama.cpp 的預設限制）。
        // 中文字元經 Tokenizer 分詞後常為 1.5 ~ 2 tokens，因此將字數截斷至 300 字內（約 450 tokens 內）以保障穩定性，
        // 且前 300 字所包含的核心主題已足夠進行語意相似度檢索。
        var truncatedTexts = texts
            .Select(t => t.Length > 300 ? t.Substring(0, 300) : t)
            .ToList();

        var requestBody = new EmbeddingRequest
        {
            Model = _config.ModelName,
            Input = truncatedTexts
        };

        var json = JsonSerializer.Serialize(requestBody, ApiJsonContext.Default.EmbeddingRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Embedding calculation failed with status {response.StatusCode}: {errBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var embeddingResponse = JsonSerializer.Deserialize(responseJson, ApiJsonContext.Default.EmbeddingResponse);

        if (embeddingResponse?.Data == null || embeddingResponse.Data.Count == 0)
        {
            throw new InvalidOperationException("Received empty data from Embedding API response.");
        }

        // Sort data by index to guarantee ordering is matching input
        var sortedData = embeddingResponse.Data.OrderBy(d => d.Index).ToList();
        var result = new List<float[]>(sortedData.Count);
        foreach (var item in sortedData)
        {
            result.Add(item.Embedding);
        }

        return result;
    }
}
