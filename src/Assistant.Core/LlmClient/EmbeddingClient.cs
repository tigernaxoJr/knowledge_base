using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Assistant.Core.Config;

namespace Assistant.Core.LlmClient;

public sealed class EmbeddingClient(HttpClient httpClient, Func<CancellationToken, Task<EmbeddingConfig>> configProvider) : IEmbeddingClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Func<CancellationToken, Task<EmbeddingConfig>> _configProvider = configProvider;

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

        var config = await _configProvider(ct);
        var endpoint = config.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("向量模型 API 端點 (Endpoint) 未配置或不是有效的絕對 URL。請先至首頁右上角的「設定」頁面配置向量模型端點與 API 金鑰！");
        }
        var requestUrl = endpoint.TrimEnd('/') + "/embeddings";

        // 避免本地或部分輕量向量模型伺服器的 512-token 實體批次大小限制（如 Ollama/llama.cpp 的預設限制）。
        // 中文字元經 Tokenizer 分詞後常為 1.5 ~ 2 tokens，因此將字數截斷至 300 字內（約 450 tokens 內）以保障穩定性，
        // 且前 300 字所包含的核心主題已足夠進行語意相似度檢索。
        var truncatedTexts = texts
            .Select(t => t.Length > 300 ? t.Substring(0, 300) : t)
            .ToList();

        var requestBody = new EmbeddingRequest
        {
            Model = config.ModelName,
            Input = truncatedTexts
        };

        var json = JsonSerializer.Serialize(requestBody, ApiJsonContext.Default.EmbeddingRequest);

        using var debugCall = LlmDebugCall.Start(new LlmDebugEvent
        {
            Kind = "embedding",
            Operation = texts.Count == 1 ? "embeddings.single" : "embeddings.batch",
            Endpoint = SafeEndpoint(requestUrl),
            Model = config.ModelName,
            InputCount = texts.Count,
            InputChars = texts.Sum(t => t.Length),
            RequestPayload = json
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
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

            // Sort data by index to guarantee ordering is matching input.
            var sortedData = embeddingResponse.Data.OrderBy(d => d.Index).ToList();
            var result = new List<float[]>(sortedData.Count);
            foreach (var item in sortedData)
            {
                result.Add(item.Embedding);
            }

            debugCall.Succeed($"{result.Count} vector(s)", result.Sum(v => v.Length));
            return result;
        }
        catch (Exception ex)
        {
            debugCall.Fail(ex);
            throw;
        }
    }

    private static string SafeEndpoint(string requestUrl)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri))
        {
            return requestUrl;
        }

        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }
}
