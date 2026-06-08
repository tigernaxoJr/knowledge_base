using Microsoft.Extensions.DependencyInjection;
using Assistant.App;

// ── 建立 DI 容器 ───────────────────────────────────────────────────────────
var services = new ServiceCollection();

// App 層服務
services.AddSingleton<ResourceLoader>();
services.AddSingleton<IpcBridge>();
services.AddSingleton<WebViewHost>();

// TODO: 註冊 Assistant.Core 服務實作
// services.AddSingleton<IConfigService, ConfigService>();
// services.AddSingleton<IIngestionService, IngestionService>();
// services.AddSingleton<IKnowledgeEntryService, KnowledgeEntryService>();
// services.AddSingleton<IVersionControlService, VersionControlService>();
// services.AddSingleton<IVectorSearchEngine, VectorSearchEngine>();
// services.AddSingleton<ILanceDbClient, LanceDbClient>();
// services.AddSingleton<IRelationalRepository, SqliteRepository>();
// services.AddSingleton<ILlmClientFactory, LlmClientFactory>();

var provider = services.BuildServiceProvider();

// ── 啟動 WebView 主視窗 ────────────────────────────────────────────────────
var host = provider.GetRequiredService<WebViewHost>();
host.Run();
