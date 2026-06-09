using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Assistant.Core.Config;

namespace Assistant.Core.Config;

public sealed class ConfigService : IConfigService
{
    private static readonly byte[] EncryptionKey = [0x5f, 0x12, 0x9a, 0xbc, 0x3d, 0x4f, 0x7e, 0x88, 0x21, 0x67, 0x44, 0x90, 0xab, 0xcd, 0xef, 0x01];
    private static readonly byte[] EncryptionIv = [0x10, 0x21, 0x32, 0x43, 0x54, 0x65, 0x76, 0x87, 0x98, 0xa9, 0xba, 0xcb, 0xdc, 0xed, 0xfe, 0x0f];
    private readonly string _configFilePath;
    private readonly HttpClient _httpClient;

    public ConfigService(string? configFilePath = null)
    {
        _configFilePath = configFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var settings = new AppSettings();

        // 1. 讀取主設定檔 (appsettings.json)
        if (File.Exists(_configFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_configFilePath, Encoding.UTF8, ct);
                var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                if (loaded != null)
                {
                    settings = loaded;
                }
            }
            catch { }
        }

        // 2. 讀取 Development 設定檔 (appsettings.Development.json)，優先級較高
        var dir = Path.GetDirectoryName(_configFilePath);
        var devFilePath = Path.Combine(dir ?? string.Empty, "appsettings.Development.json");
        if (File.Exists(devFilePath))
        {
            try
            {
                var devJson = await File.ReadAllTextAsync(devFilePath, Encoding.UTF8, ct);
                var devSettings = JsonSerializer.Deserialize(devJson, AppSettingsJsonContext.Default.AppSettings);
                if (devSettings != null)
                {
                    // 合併設定
                    if (devSettings.SummaryLimit > 0)
                    {
                        settings.SummaryLimit = devSettings.SummaryLimit;
                    }
                    if (devSettings.LlmConfig != null)
                    {
                        if (!string.IsNullOrEmpty(devSettings.LlmConfig.Endpoint)) settings.LlmConfig.Endpoint = devSettings.LlmConfig.Endpoint;
                        if (!string.IsNullOrEmpty(devSettings.LlmConfig.ApiKey)) settings.LlmConfig.ApiKey = devSettings.LlmConfig.ApiKey;
                        if (!string.IsNullOrEmpty(devSettings.LlmConfig.ModelName)) settings.LlmConfig.ModelName = devSettings.LlmConfig.ModelName;
                    }
                    if (devSettings.EmbeddingConfig != null)
                    {
                        if (!string.IsNullOrEmpty(devSettings.EmbeddingConfig.Endpoint)) settings.EmbeddingConfig.Endpoint = devSettings.EmbeddingConfig.Endpoint;
                        if (!string.IsNullOrEmpty(devSettings.EmbeddingConfig.ApiKey)) settings.EmbeddingConfig.ApiKey = devSettings.EmbeddingConfig.ApiKey;
                        if (!string.IsNullOrEmpty(devSettings.EmbeddingConfig.ModelName)) settings.EmbeddingConfig.ModelName = devSettings.EmbeddingConfig.ModelName;
                    }
                }
            }
            catch { }
        }

        // 3. 解密 ApiKeys (若為加密格式則解密，若非加密格式會自動 fallback 回傳明文)
        if (!string.IsNullOrEmpty(settings.LlmConfig.ApiKey))
        {
            settings.LlmConfig.ApiKey = DecryptString(settings.LlmConfig.ApiKey);
        }
        if (!string.IsNullOrEmpty(settings.EmbeddingConfig.ApiKey))
        {
            settings.EmbeddingConfig.ApiKey = DecryptString(settings.EmbeddingConfig.ApiKey);
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        // Clone settings to avoid mutating the original object in memory
        var clone = new AppSettings
        {
            SummaryLimit = settings.SummaryLimit,
            LlmConfig = new LlmConfig
            {
                Endpoint = settings.LlmConfig.Endpoint,
                ApiKey = settings.LlmConfig.ApiKey,
                ModelName = settings.LlmConfig.ModelName
            },
            EmbeddingConfig = new EmbeddingConfig
            {
                Endpoint = settings.EmbeddingConfig.Endpoint,
                ApiKey = settings.EmbeddingConfig.ApiKey,
                ModelName = settings.EmbeddingConfig.ModelName
            }
        };

        // Encrypt ApiKeys
        if (!string.IsNullOrEmpty(clone.LlmConfig.ApiKey))
        {
            clone.LlmConfig.ApiKey = EncryptString(clone.LlmConfig.ApiKey);
        }
        if (!string.IsNullOrEmpty(clone.EmbeddingConfig.ApiKey))
        {
            clone.EmbeddingConfig.ApiKey = EncryptString(clone.EmbeddingConfig.ApiKey);
        }

        var json = JsonSerializer.Serialize(clone, AppSettingsJsonContext.Default.AppSettings);
        var dir = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(_configFilePath, json, Encoding.UTF8, ct);
    }

    public async Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(
        string endpoint, string apiKey, string modelName, CancellationToken ct = default)
    {
        try
        {
            // Prepare a lightweight request payload (typically chat completion with minimal tokens)
            var requestUrl = endpoint.TrimEnd('/') + "/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            var requestBody = new Assistant.Core.LlmClient.ChatCompletionRequest
            {
                Model = modelName,
                Messages =
                [
                    new Assistant.Core.LlmClient.ChatMessage { Role = "user", Content = "ping" }
                ]
            };

            var jsonPayload = JsonSerializer.Serialize(requestBody, Assistant.Core.LlmClient.ApiJsonContext.Default.ChatCompletionRequest);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            var errBody = await response.Content.ReadAsStringAsync(ct);
            return (false, $"HTTP {response.StatusCode}: {errBody}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string EncryptString(string plainText)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            aes.IV = EncryptionIv;
            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return plainText; // Fallback
        }
    }

    private static string DecryptString(string cipherText)
    {
        try
        {
            var buffer = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            aes.IV = EncryptionIv;
            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(buffer);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
        catch
        {
            return cipherText; // Fallback
        }
    }
}
