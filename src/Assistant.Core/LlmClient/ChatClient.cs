using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Assistant.Core.Config;

namespace Assistant.Core.LlmClient;

public sealed class ChatClient(HttpClient httpClient, Func<CancellationToken, Task<LlmConfig>> configProvider) : IChatClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Func<CancellationToken, Task<LlmConfig>> _configProvider = configProvider;

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var config = await _configProvider(ct);
        var endpoint = config.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("大模型 API 端點 (Endpoint) 未配置或不是有效的絕對 URL。請先至首頁右上角的「設定」頁面配置大模型端點與 API 金鑰！");
        }
        var requestUrl = endpoint.TrimEnd('/') + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        var requestBody = new ChatCompletionRequest
        {
            Model = config.ModelName,
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userMessage }
            ]
        };

        var json = JsonSerializer.Serialize(requestBody, ApiJsonContext.Default.ChatCompletionRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"LLM Chat Completion failed with status {response.StatusCode}: {errBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var chatResponse = JsonSerializer.Deserialize(responseJson, ApiJsonContext.Default.ChatCompletionResponse);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new InvalidOperationException("Received empty choices from LLM API response.");
        }

        return chatResponse.Choices[0].Message.Content;
    }
}
