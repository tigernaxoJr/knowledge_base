using System.Text.Json.Serialization;

namespace Assistant.Core.Config;

/// <summary>LLM 端點設定</summary>
public sealed class LlmConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
}

/// <summary>Embedding 端點設定</summary>
public sealed class EmbeddingConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
}

/// <summary>應用程式設定根物件</summary>
public sealed class AppSettings
{
    public int SummaryLimit { get; set; } = 200;
    public LlmConfig LlmConfig { get; set; } = new();
    public EmbeddingConfig EmbeddingConfig { get; set; } = new();
}

/// <summary>
/// AOT 相容：Source Generator 驅動的 JSON 序列化上下文。
/// 所有需要序列化的 DTO 皆須在此標記 [JsonSerializable]。
/// </summary>
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LlmConfig))]
[JsonSerializable(typeof(EmbeddingConfig))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AppSettingsJsonContext : JsonSerializerContext { }
