using Assistant.Core.Config;

namespace Assistant.Core.LlmClient;

public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();

    private AppSettings _settings;
    private IChatClient? _cachedChatClient;
    private IEmbeddingClient? _cachedEmbeddingClient;

    public LlmClientFactory(IConfigService configService, HttpClient? httpClient = null)
    {
        _configService = configService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        
        // Synchronously load settings for the initial construction
        _settings = Task.Run(async () => await _configService.LoadAsync()).GetAwaiter().GetResult();
    }

    public IChatClient CreateChatClient()
    {
        lock (_lock)
        {
            return _cachedChatClient ??= new ChatClient(_httpClient, _settings.LlmConfig);
        }
    }

    public IEmbeddingClient CreateEmbeddingClient()
    {
        lock (_lock)
        {
            return _cachedEmbeddingClient ??= new EmbeddingClient(_httpClient, _settings.EmbeddingConfig);
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _settings = Task.Run(async () => await _configService.LoadAsync()).GetAwaiter().GetResult();
            _cachedChatClient = null;
            _cachedEmbeddingClient = null;
        }
    }
}
