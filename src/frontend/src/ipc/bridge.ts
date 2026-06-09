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

interface IpcDebugEventMessage {
  command: 'ingest.debug.event'
  requestId: string
  event: LlmDebugEvent
}

export interface LlmDebugEvent {
  id: string
  kind: string
  operation: string
  status: 'started' | 'completed' | 'failed' | 'abandoned'
  startedAt: string
  completedAt?: string
  durationMs?: number
  endpoint?: string
  model?: string
  inputCount?: number
  inputChars?: number
  systemPromptChars?: number
  userMessageChars?: number
  responseChars?: number
  preview?: string
  error?: string
  requestPayload?: string
}

export interface IngestDebugResult {
  events: LlmDebugEvent[]
}

type PendingResolve = (value: unknown) => void
type PendingReject = (reason: Error) => void

const pendingRequests = new Map<string, [PendingResolve, PendingReject]>()

function isWebView2Available(): boolean {
  return typeof window !== 'undefined' &&
    // @ts-expect-error WebView2 injects chrome.webview.
    typeof window.chrome?.webview?.postMessage === 'function'
}

if (isWebView2Available()) {
  // @ts-expect-error WebView2 injects chrome.webview.
  window.chrome.webview.addEventListener('message', (event: MessageEvent<string>) => {
    let response: IpcResponse
    try {
      response = JSON.parse(event.data) as IpcResponse
    } catch {
      console.error('[IPC] Failed to parse response:', event.data)
      return
    }

    const debugMessage = response as unknown as IpcDebugEventMessage
    if (debugMessage.command === 'ingest.debug.event' && debugMessage.event) {
      window.dispatchEvent(new CustomEvent<LlmDebugEvent>('ingest-debug-event', { detail: debugMessage.event }))
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

function mockDebugEvents(command: string): IngestDebugResult {
  const now = Date.now()
  const completed = (offset: number, duration: number, event: Partial<LlmDebugEvent>): LlmDebugEvent => ({
    id: crypto.randomUUID(),
    kind: event.kind ?? 'chat',
    operation: event.operation ?? 'chat.completions',
    status: 'completed',
    startedAt: new Date(now + offset).toISOString(),
    completedAt: new Date(now + offset + duration).toISOString(),
    durationMs: duration,
    endpoint: event.endpoint ?? 'https://example.local/chat/completions',
    model: event.model ?? 'debug-model',
    inputCount: event.inputCount,
    inputChars: event.inputChars,
    systemPromptChars: event.systemPromptChars,
    userMessageChars: event.userMessageChars,
    responseChars: event.responseChars,
    preview: event.preview,
    requestPayload: event.requestPayload,
  })

  const events = [
    completed(0, 920, {
      kind: 'chat',
      operation: command === 'ingest.batch' ? 'outline document 1' : 'outline',
      systemPromptChars: 320,
      userMessageChars: 1600,
      responseChars: 210,
      preview: 'Mock outline result for debugging.',
      requestPayload: JSON.stringify({
        model: 'debug-chat',
        messages: [
          { role: 'system', content: 'You are an outline generator...' },
          { role: 'user', content: 'Document text content...' }
        ]
      }, null, 2)
    }),
    completed(950, 760, {
      kind: 'embedding',
      operation: command === 'ingest.batch' ? 'embeddings.batch' : 'embeddings.single',
      inputCount: command === 'ingest.batch' ? 4 : 1,
      inputChars: 840,
      responseChars: 6144,
      preview: command === 'ingest.batch' ? '4 vector(s)' : '1 vector(s)',
      endpoint: 'https://example.local/embeddings',
      requestPayload: JSON.stringify({
        model: 'debug-embedding',
        input: command === 'ingest.batch' ? ['Outline 1', 'Outline 2', 'Outline 3', 'Outline 4'] : ['Outline 1']
      }, null, 2)
    }),
    completed(1740, 1250, {
      kind: 'chat',
      operation: command === 'ingest.batch' ? 'cluster title / merge' : 'title generation',
      systemPromptChars: 250,
      userMessageChars: 900,
      responseChars: 48,
      preview: 'Mock generated knowledge title.',
      requestPayload: JSON.stringify({
        model: 'debug-chat',
        messages: [
          { role: 'system', content: 'You are a knowledge merger...' },
          { role: 'user', content: 'Merged details...' }
        ]
      }, null, 2)
    }),
  ]

  return { events }
}

function getMockData(command: string, payload: unknown): unknown {
  switch (command) {
    case 'ingest':
    case 'ingest.batch':
      return (payload as { debug?: boolean } | undefined)?.debug ? mockDebugEvents(command) : null
    case 'search':
      return [
        { entryId: '11111111-1111-1111-1111-111111111111', title: 'Docker deployment notes', score: 0.98 },
        { entryId: '22222222-2222-2222-2222-222222222222', title: 'ASP.NET Core Native AOT', score: 0.91 },
      ]
    case 'entry.get':
      return {
        entryId: (payload as { entryId: string }).entryId,
        title: 'Sample entry',
        content: '# Sample entry\n\nThis is mock content.',
        version: 1,
        updatedAt: new Date().toISOString(),
      }
    case 'entry.update':
      return {
        ...(payload as { entryId: string; title: string; content: string }),
        version: 2,
        updatedAt: new Date().toISOString(),
      }
    case 'entry.history':
      return [
        { version: 1, contentSnapshot: 'Initial content', archivedAt: new Date().toISOString() },
      ]
    case 'cluster.list':
      return [
        {
          clusterId: 'c1111111-1111-1111-1111-111111111111',
          name: 'Sample cluster',
          entries: [
            { entryId: '11111111-1111-1111-1111-111111111111', title: 'Docker deployment notes', version: 1, updatedAt: new Date().toISOString() },
          ],
        },
      ]
    case 'config.load': {
      try {
        const stored = localStorage.getItem('kb_mock_settings')
        if (stored) return JSON.parse(stored)
      } catch {
        // Ignore malformed local mock settings.
      }
      return {
        llmConfig: { endpoint: 'https://api.example.com/v1', apiKey: '', modelName: 'debug-chat' },
        embeddingConfig: { endpoint: 'https://api.example.com/v1', apiKey: '', modelName: 'debug-embedding' },
        clusteringConfig: { eps: 0.25, minPts: 2 },
      }
    }
    case 'config.save':
      localStorage.setItem('kb_mock_settings', JSON.stringify(payload))
      return null
    case 'config.test':
      return { success: true }
    default:
      return null
  }
}

export function invoke<T = unknown>(command: string, payload?: unknown): Promise<T> {
  if (!isWebView2Available()) {
    console.warn(`[IPC Mock] command="${command}"`, payload)
    return new Promise<T>((resolve) => {
      setTimeout(() => resolve(getMockData(command, payload) as T), 400)
    })
  }

  return new Promise<T>((resolve, reject) => {
    const requestId = crypto.randomUUID()
    const request: IpcRequest = { command, requestId, payload }

    pendingRequests.set(requestId, [
      (data) => resolve(data as T),
      reject,
    ])

    // @ts-expect-error WebView2 injects chrome.webview.
    window.chrome.webview.postMessage(JSON.stringify(request))
  })
}

export const ipc = {
  ingest: (content: string, source: string, debug = false) =>
    invoke<IngestDebugResult | null>('ingest', { content, source, debug }),

  ingestBatch: (items: Array<{ content: string; source: string }>, debug = false) =>
    invoke<IngestDebugResult | null>('ingest.batch', { items, debug }),

  search: (query: string) =>
    invoke<Array<{ entryId: string; title: string; score: number }>>('search', { query }),

  entry: {
    get: (entryId: string) =>
      invoke<{ entryId: string; title: string; content: string; version: number; updatedAt: string }>('entry.get', { entryId }),
    update: (entryId: string, title: string, content: string) =>
      invoke<{ entryId: string; title: string; content: string; version: number; updatedAt: string }>('entry.update', { entryId, title, content }),
    rollback: (entryId: string, version: number) =>
      invoke<void>('entry.rollback', { entryId, version }),
    history: (entryId: string) =>
      invoke<Array<{ version: number; contentSnapshot: string; archivedAt: string }>>('entry.history', { entryId }),
    delete: (entryId: string) =>
      invoke<void>('entry.delete', { entryId }),
  },

  cluster: {
    list: () =>
      invoke<Array<{ clusterId: string; name: string; entries: Array<{ entryId: string; title: string; version: number; updatedAt: string }> }>>('cluster.list'),
    recluster: () =>
      invoke<void>('cluster.recluster'),
  },

  config: {
    load: () =>
      invoke<{
        llmConfig: { endpoint: string; apiKey: string; modelName: string }
        embeddingConfig: { endpoint: string; apiKey: string; modelName: string }
        clusteringConfig: { eps: number; minPts: number }
      }>('config.load'),
    save: (settings: {
      llmConfig: { endpoint: string; apiKey: string; modelName: string }
      embeddingConfig: { endpoint: string; apiKey: string; modelName: string }
      clusteringConfig: { eps: number; minPts: number }
    }) =>
      invoke<void>('config.save', settings),
    test: (endpoint: string, apiKey: string, modelName: string) =>
      invoke<{ success: boolean; errorMessage?: string }>('config.test', { endpoint, apiKey, modelName }),
  },
} as const
