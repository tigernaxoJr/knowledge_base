/**
 * IPC Bridge — 前端與 .NET 後端通訊層
 *
 * 通訊協議：
 *   前端 → 後端：window.chrome.webview.postMessage(JSON)
 *   後端 → 前端：window.chrome.webview 'message' 事件
 *
 * 請求格式：{ command, requestId, payload }
 * 回應格式：{ requestId, success, data?, error? }
 */

// ── 型別定義 ──────────────────────────────────────────────────────────────────

interface IpcRequest {
  command: string
  requestId: string
  payload?: unknown
}

interface IpcResponse {
  requestId: string
  success: boolean
  data?: unknown
  error?: string
}

// ── 待處理請求的 Promise 映射表 ───────────────────────────────────────────────

type PendingResolve = (value: unknown) => void
type PendingReject  = (reason: Error) => void

const pendingRequests = new Map<string, [PendingResolve, PendingReject]>()

// ── 監聽後端回應（全域初始化一次）────────────────────────────────────────────

function isWebView2Available(): boolean {
  return typeof window !== 'undefined' &&
    // @ts-expect-error chrome.webview 為 WebView2 注入的非標準屬性
    typeof window.chrome?.webview?.postMessage === 'function'
}

if (isWebView2Available()) {
  // @ts-expect-error WebView2 非標準 API
  window.chrome.webview.addEventListener('message', (event: MessageEvent<string>) => {
    let response: IpcResponse
    try {
      response = JSON.parse(event.data) as IpcResponse
    } catch {
      console.error('[IPC] Failed to parse response:', event.data)
      return
    }

    const pending = pendingRequests.get(response.requestId)
    if (!pending) return

    pendingRequests.delete(response.requestId)
    const [resolve, reject] = pending

    if (response.success) {
      resolve(response.data)
    } else {
      reject(new Error(response.error ?? 'Unknown IPC error'))
    }
  })
}

// ── 核心發送函式 ──────────────────────────────────────────────────────────────

function getMockData(command: string, payload: unknown): unknown {
  switch (command) {
    case 'ingest':
      return null;
    case 'search': {
      const q = ((payload as { query?: string })?.query || '').toLowerCase();
      return [
        {
          entryId: '11111111-1111-1111-1111-111111111111',
          title: 'Docker 容器化部署與微服務架構實作指南',
          score: q.includes('docker') || q.includes('容器') ? 0.98 : 0.85
        },
        {
          entryId: '22222222-2222-2222-2222-222222222222',
          title: 'ASP.NET Core 10 Web API 架構設計與 Native AOT 編譯實務',
          score: q.includes('net') || q.includes('core') || q.includes('aot') ? 0.96 : 0.75
        },
        {
          entryId: '33333333-3333-3333-3333-333333333333',
          title: 'Tailwind CSS v4 擬物化與毛玻璃效果視覺設計規範',
          score: q.includes('css') || q.includes('tailwind') || q.includes('設計') ? 0.92 : 0.62
        }
      ].filter(item => q === '' || item.score > 0.65).sort((a, b) => b.score - a.score);
    }
    case 'entry.get': {
      const id = (payload as { entryId: string }).entryId;
      if (id === '11111111-1111-1111-1111-111111111111') {
        return {
          entryId: id,
          title: 'Docker 容器化部署與微服務架構實作指南',
          content: `# Docker 容器化部署與微服務架構實作指南\n\n本篇知識條目詳細定義了企業級 Docker 容器化運作的最佳實踐。\n\n## 核心設計原則\n1. **映像檔極小化**：優先選用 Alpine 或 Distroless 做為執行基底，阻絕安全漏洞。\n2. **多階段建置 (Multi-stage Build)**：僅將編譯後的發布產物拷貝至 Runtime 映像檔中，使體積減少 80% 以上。\n3. **非 Root 執行**：在 Dockerfile 結尾指定 \`USER 1000\`，避免最高權限被容器內惡意代碼利用。\n\n## 最佳 Dockerfile 示範\n\`\`\`dockerfile\nFROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\nWORKDIR /src\nCOPY . .\nRUN dotnet publish -c Release -o /app\n\nFROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine\nWORKDIR /app\nCOPY --from=build /app .\nUSER 1000\nENTRYPOINT ["dotnet", "Assistant.App.dll"]\n\`\`\``,
          version: 2,
          updatedAt: new Date().toISOString()
        };
      }
      if (id === '22222222-2222-2222-2222-222222222222') {
        return {
          entryId: id,
          title: 'ASP.NET Core 10 Web API 架構設計與 Native AOT 編譯實務',
          content: `# ASP.NET Core 10 Web API 架構設計與 Native AOT\n\n本條目旨在說明如何利用 .NET 10 Native AOT 實現毫秒級啟動與極低記憶體佔用。\n\n## AOT 技術限制與最佳實踐\n- **禁用反射 (No Reflection)**：所有 JSON 序列化皆必須依賴 \`JsonSerializerContext\` 編譯期靜態生成程式碼。\n- **依賴檢驗**：使用 \`<EnableTrimAnalyzer>true</EnableTrimAnalyzer>\` 在編譯期抓取不相容的 NuGet 套件。\n- **輕量化 API**：使用 Minimum APIs 可減少啟動時的中間件開銷。`,
          version: 1,
          updatedAt: new Date(Date.now() - 3600000 * 24).toISOString()
        };
      }
      return {
        entryId: id,
        title: 'Tailwind CSS v4 擬物化與毛玻璃效果視覺設計規範',
        content: `# Tailwind CSS v4 擬物化與毛玻璃效果\n\n本文檔記錄了個人知識庫前台所使用的極致視覺系統設計。\n\n## 毛玻璃效果 (Glassmorphism)\n- 背景模糊度：\`backdrop-blur-md\`\n- 半透明背景：\`bg-white/5\` 或 \`bg-black/10\`\n- 微細白邊框：\`border border-white/10\`\n\n## 漸層炫光與微動畫\n- 懸停縮放：\`hover:scale-[1.02] active:scale-[0.98]\`\n- 文字漸層：\`bg-gradient-to-r from-indigo-400 to-cyan-400 bg-clip-text text-transparent\``,
        version: 3,
        updatedAt: new Date(Date.now() - 3600000 * 5).toISOString()
      };
    }
    case 'entry.rollback':
      return null;
    case 'entry.history': {
      return [
        { version: 3, contentSnapshot: '這是最新版本 3 的內容快照：定義了毛玻璃邊界。', archivedAt: new Date().toISOString() },
        { version: 2, contentSnapshot: '這是版本 2 的歷史快照：新增了漸層文字與卡片縮放。', archivedAt: new Date(Date.now() - 3600000 * 3).toISOString() },
        { version: 1, contentSnapshot: '這是原始版本 1 的快照：建立了基本的基礎樣式骨架。', archivedAt: new Date(Date.now() - 3600000 * 24).toISOString() }
      ];
    }
    case 'config.load': {
      try {
        const stored = localStorage.getItem('kb_mock_settings');
        if (stored) return JSON.parse(stored);
      } catch {}
      return {
        llmConfig: { endpoint: 'https://api.deepseek.com/v1', apiKey: 'sk-deepseek-test-key-xxxxxxxxxx', modelName: 'deepseek-chat' },
        embeddingConfig: { endpoint: 'https://api.openai.com/v1', apiKey: 'sk-openai-test-key-yyyyyyyyyy', modelName: 'text-embedding-3-small' }
      };
    }
    case 'config.save': {
      localStorage.setItem('kb_mock_settings', JSON.stringify(payload));
      return null;
    }
    case 'config.test':
      return { success: true };
    default:
      return null;
  }
}

/**
 * 發送 IPC 命令至後端，回傳 Promise。
 * 在開發模式（無 WebView2 環境）下，回傳 Mock 資料。
 */
export function invoke<T = unknown>(command: string, payload?: unknown): Promise<T> {
  if (!isWebView2Available()) {
    console.warn(`[IPC Mock] command="${command}"`, payload)
    return new Promise<T>((resolve) => {
      setTimeout(() => {
        resolve(getMockData(command, payload) as T)
      }, 400) // 模擬網路與 IPC 通訊延遲
    })
  }

  return new Promise<T>((resolve, reject) => {
    const requestId = crypto.randomUUID()
    const request: IpcRequest = { command, requestId, payload }

    pendingRequests.set(requestId, [
      (data) => resolve(data as T),
      reject,
    ])

    // @ts-expect-error WebView2 非標準 API
    window.chrome.webview.postMessage(JSON.stringify(request))
  })
}

// ── 型別安全的命令封裝 ────────────────────────────────────────────────────────

export const ipc = {
  /** 導入單份文件 */
  ingest: (content: string, source: string) =>
    invoke<void>('ingest', { content, source }),

  /** 知識語意搜尋 */
  search: (query: string) =>
    invoke<Array<{ entryId: string; title: string; score: number }>>('search', { query }),

  /** 取得知識條目 */
  entry: {
    get: (entryId: string) =>
      invoke<{ entryId: string; title: string; content: string; version: number; updatedAt: string }>('entry.get', { entryId }),
    rollback: (entryId: string, version: number) =>
      invoke<void>('entry.rollback', { entryId, version }),
    history: (entryId: string) =>
      invoke<Array<{ version: number; contentSnapshot: string; archivedAt: string }>>('entry.history', { entryId }),
  },

  /** 設定管理 */
  config: {
    load: () =>
      invoke<{ llmConfig: { endpoint: string; apiKey: string; modelName: string }; embeddingConfig: { endpoint: string; apiKey: string; modelName: string } }>('config.load'),
    save: (settings: { llmConfig: { endpoint: string; apiKey: string; modelName: string }; embeddingConfig: { endpoint: string; apiKey: string; modelName: string } }) =>
      invoke<void>('config.save', settings),
    test: (endpoint: string, apiKey: string, modelName: string) =>
      invoke<{ success: boolean; errorMessage?: string }>('config.test', { endpoint, apiKey, modelName }),
  },
} as const;
