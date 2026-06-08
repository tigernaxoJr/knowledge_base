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

/**
 * 發送 IPC 命令至後端，回傳 Promise。
 * 在開發模式（無 WebView2 環境）下，回傳 Mock 資料或拋出提示。
 */
export function invoke<T = unknown>(command: string, payload?: unknown): Promise<T> {
  if (!isWebView2Available()) {
    // Dev Server 模式：提示開發者（可在此加入 Mock 實作）
    console.warn(`[IPC Mock] command="${command}"`, payload)
    return Promise.reject(new Error(`[IPC] WebView2 not available. Command: ${command}`))
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

  /** 取得知識條目 */
  entry: {
    get: (entryId: string) =>
      invoke<{ entryId: string; title: string; content: string; version: number }>('entry.get', { entryId }),
    rollback: (entryId: string, targetVersion: number) =>
      invoke<void>('entry.rollback', { entryId, targetVersion }),
  },

  /** 設定管理 */
  config: {
    load: () =>
      invoke<{ llmConfig: object; embeddingConfig: object }>('config.load'),
    save: (settings: object) =>
      invoke<void>('config.save', settings),
    test: (endpoint: string, apiKey: string, modelName: string) =>
      invoke<{ success: boolean; error?: string }>('config.test', { endpoint, apiKey, modelName }),
  },
} as const
