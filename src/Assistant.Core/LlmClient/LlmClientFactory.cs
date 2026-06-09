using Assistant.Core.Config;

namespace Assistant.Core.LlmClient;

public sealed class LlmClientFactory(IConfigService configService, HttpClient? httpClient = null) : ILlmClientFactory
{
    private readonly IConfigService _configService = configService;
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    private readonly object _lock = new();

    private Task<AppSettings>? _settingsTask;
    private IChatClient? _cachedChatClient;
    private IEmbeddingClient? _cachedEmbeddingClient;

    private Task<AppSettings> GetSettingsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return _settingsTask ??= _configService.LoadAsync(ct);
        }
    }

    public IChatClient CreateChatClient()
    {
        lock (_lock)
        {
            return _cachedChatClient ??= new ChatClient(_httpClient, async (ct) =>
            {
                var settings = await GetSettingsAsync(ct);
                return settings.LlmConfig;
            });
        }
    }

    public IEmbeddingClient CreateEmbeddingClient()
    {
        lock (_lock)
        {
            return _cachedEmbeddingClient ??= new EmbeddingClient(_httpClient, async (ct) =>
            {
                var settings = await GetSettingsAsync(ct);
                return settings.EmbeddingConfig;
            });
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _settingsTask = null;
            _cachedChatClient = null;
            _cachedEmbeddingClient = null;
        }
    }
}
