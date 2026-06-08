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

        var requestUrl = _config.Endpoint.TrimEnd('/') + "/embeddings";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        var requestBody = new EmbeddingRequest
        {
            Model = _config.ModelName,
            Input = texts.ToList()
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
