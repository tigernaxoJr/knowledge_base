using Microsoft.Extensions.DependencyInjection;
using System;

namespace Assistant.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // ── 建立 DI 容器 ───────────────────────────────────────────────────────────
        var services = new ServiceCollection();

        // App 層服務
        services.AddSingleton<ResourceLoader>();
        services.AddSingleton<IpcBridge>();
        services.AddSingleton<WebViewHost>();

        // 註冊 Assistant.Core 服務實作
        services.AddSingleton<Assistant.Core.Config.IConfigService, Assistant.Core.Config.ConfigService>();
        services.AddSingleton<Assistant.Core.Storage.IRelationalRepository, Assistant.Core.Storage.SqliteRepository>();
        services.AddSingleton<Assistant.Core.Storage.ILanceDbClient, Assistant.Core.Storage.LanceDbClient>();
        services.AddSingleton<Assistant.Core.LlmClient.ILlmClientFactory, Assistant.Core.LlmClient.LlmClientFactory>();
        services.AddSingleton<Assistant.Core.Prompts.IPromptProvider, Assistant.Core.Prompts.DefaultPromptProvider>();
        services.AddSingleton<Assistant.Core.Ingestion.IOutlineGenerator, Assistant.Core.Ingestion.OutlineGenerator>();
        services.AddSingleton<Assistant.Core.Search.IVectorSearchEngine, Assistant.Core.Search.VectorSearchEngine>();
        services.AddSingleton<Assistant.Core.Search.IRoutingDecision, Assistant.Core.Search.RoutingDecision>();
        services.AddSingleton<Assistant.Core.KnowledgeBase.IVersionControlService, Assistant.Core.KnowledgeBase.VersionControlService>();
        services.AddSingleton<Assistant.Core.KnowledgeBase.IKnowledgeEntryService, Assistant.Core.KnowledgeBase.KnowledgeEntryService>();
        services.AddSingleton<Assistant.Core.Clustering.IHdbscanEngine, Assistant.Core.Clustering.HdbscanEngine>();
        services.AddSingleton<Assistant.Core.Ingestion.IIngestionService, Assistant.Core.Ingestion.IngestionService>();

        var provider = services.BuildServiceProvider();

        // ── 啟動 WebView 主視窗 ────────────────────────────────────────────────────
        using var host = provider.GetRequiredService<WebViewHost>();
        host.Run();
    }
}
