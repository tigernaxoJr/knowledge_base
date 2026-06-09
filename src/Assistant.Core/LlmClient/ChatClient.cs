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

        using var debugCall = LlmDebugCall.Start(new LlmDebugEvent
        {
            Kind = "chat",
            Operation = "chat.completions",
            Endpoint = SafeEndpoint(requestUrl),
            Model = config.ModelName,
            SystemPromptChars = systemPrompt.Length,
            UserMessageChars = userMessage.Length,
            InputChars = systemPrompt.Length + userMessage.Length,
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
                throw new HttpRequestException($"LLM Chat Completion failed with status {response.StatusCode}: {errBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var chatResponse = JsonSerializer.Deserialize(responseJson, ApiJsonContext.Default.ChatCompletionResponse);

            if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
            {
                throw new InvalidOperationException("Received empty choices from LLM API response.");
            }

            var content = StripThoughtBlocks(chatResponse.Choices[0].Message.Content);
            debugCall.Succeed(Preview(content), content.Length);
            return content;
        }
        catch (Exception ex)
        {
            debugCall.Fail(ex);
            throw;
        }
    }

    private static string StripThoughtBlocks(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        string[] tags = ["thought", "think"];
        bool replaced;
        do
        {
            replaced = false;
            foreach (var tag in tags)
            {
                var startTag = $"<{tag}";
                var endTag = $"</{tag}>";
                
                int startIndex = content.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
                if (startIndex != -1)
                {
                    int startTagEnd = content.IndexOf('>', startIndex);
                    if (startTagEnd != -1)
                    {
                        int endIndex = content.IndexOf(endTag, startTagEnd, StringComparison.OrdinalIgnoreCase);
                        if (endIndex != -1)
                        {
                            content = content.Remove(startIndex, endIndex + endTag.Length - startIndex);
                            replaced = true;
                            break;
                        }
                    }
                }
            }
        } while (replaced);

        return content.Trim();
    }

    private static string SafeEndpoint(string requestUrl)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri))
        {
            return requestUrl;
        }

        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }

    private static string Preview(string value)
    {
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= 240 ? value : value[..240] + "...";
    }
}
