using Assistant.Core.Config;
using Xunit;

namespace Assistant.Core.Tests;

public class ConfigTests
{
    [Fact]
    public async Task SaveAndLoadConfig_ShouldPreserveSettingsAndObfuscateApiKey()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var configService = new ConfigService(tempFile);
            var settings = new AppSettings
            {
                LlmConfig = new LlmConfig
                {
                    Endpoint = "https://api.openai.com/v1",
                    ApiKey = "sk-1234567890",
                    ModelName = "gpt-4o"
                },
                EmbeddingConfig = new EmbeddingConfig
                {
                    Endpoint = "https://api.openai.com/v1",
                    ApiKey = "sk-abcdef",
                    ModelName = "text-embedding-3-small"
                }
            };

            // Save settings
            await configService.SaveAsync(settings);

            // Verify the raw JSON file does not contain plain text API keys
            var rawJson = await File.ReadAllTextAsync(tempFile);
            Assert.DoesNotContain("sk-1234567890", rawJson);
            Assert.DoesNotContain("sk-abcdef", rawJson);

            // Load settings
            var loaded = await configService.LoadAsync();
            
            Assert.NotNull(loaded);
            Assert.Equal("https://api.openai.com/v1", loaded.LlmConfig.Endpoint);
            Assert.Equal("sk-1234567890", loaded.LlmConfig.ApiKey);
            Assert.Equal("gpt-4o", loaded.LlmConfig.ModelName);
            
            Assert.Equal("https://api.openai.com/v1", loaded.EmbeddingConfig.Endpoint);
            Assert.Equal("sk-abcdef", loaded.EmbeddingConfig.ApiKey);
            Assert.Equal("text-embedding-3-small", loaded.EmbeddingConfig.ModelName);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
