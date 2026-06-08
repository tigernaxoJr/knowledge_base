using System.Text.Json.Serialization;

namespace Assistant.Core.LlmClient;

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }
    
    [JsonPropertyName("content")]
    public required string Content { get; set; }
}

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }
    
    [JsonPropertyName("messages")]
    public required List<ChatMessage> Messages { get; set; }
}

public sealed class ChatCompletionChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }
    
    [JsonPropertyName("message")]
    public required ChatMessage Message { get; set; }
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatCompletionChoice> Choices { get; set; } = [];
}

public sealed class EmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }
    
    [JsonPropertyName("input")]
    public required List<string> Input { get; set; }
}

public sealed class EmbeddingData
{
    [JsonPropertyName("index")]
    public int Index { get; set; }
    
    [JsonPropertyName("embedding")]
    public required float[] Embedding { get; set; }
}

public sealed class EmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<EmbeddingData> Data { get; set; } = [];
}

[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
public partial class ApiJsonContext : JsonSerializerContext { }
