using System.Text.Json.Serialization;

namespace Assistant.Core.LlmClient;

public sealed record ChatMessage
{
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
}

public sealed record ChatCompletionRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("messages")] public required List<ChatMessage> Messages { get; init; }
}

public sealed record ChatCompletionChoice
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("message")] public required ChatMessage Message { get; init; }
}

public sealed record ChatCompletionResponse
{
    [JsonPropertyName("choices")] public List<ChatCompletionChoice> Choices { get; init; } = [];
}

public sealed record EmbeddingRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("input")] public required List<string> Input { get; init; }
}

public sealed record EmbeddingData
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("embedding")] public required float[] Embedding { get; init; }
}

public sealed record EmbeddingResponse
{
    [JsonPropertyName("data")] public List<EmbeddingData> Data { get; init; } = [];
}

[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
public partial class ApiJsonContext : JsonSerializerContext { }
