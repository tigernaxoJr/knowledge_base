using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Assistant.Core.Config;

namespace Assistant.Core.LlmClient;

public sealed class ChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly LlmConfig _config;

    public ChatClient(HttpClient httpClient, LlmConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var requestUrl = _config.Endpoint.TrimEnd('/') + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        var requestBody = new ChatCompletionRequest
        {
            Model = _config.ModelName,
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
