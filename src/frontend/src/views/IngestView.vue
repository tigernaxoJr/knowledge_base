<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { ipc, type IngestDebugResult, type LlmDebugEvent } from '../ipc/bridge'

type BatchFileItem = { name: string; content: string; size: number }

const activeTab = ref<'single' | 'batch'>('single')
const ingestSource = ref('')
const ingestContent = ref('')
const batchFiles = ref<BatchFileItem[]>([])
const isIngesting = ref(false)
const ingestStep = ref(0)
const showIngestSuccess = ref(false)
const dragOver = ref(false)
const debugEnabled = ref(true)
const debugEvents = ref<LlmDebugEvent[]>([])
const lastError = ref('')
const expandedPayloads = ref<Record<string, boolean>>({})

function togglePayload(id: string) {
  expandedPayloads.value[id] = !expandedPayloads.value[id]
}

function formatJson(val?: string) {
  if (!val) return ''
  try {
    const parsed = JSON.parse(val)
    return JSON.stringify(parsed, null, 2)
  } catch {
    return val
  }
}

const singleSteps = ['Read document', 'Generate outline', 'Create embedding', 'Route entry', 'Write knowledge entry', 'Done']
const batchSteps = ['Read files', 'Generate outlines', 'Create batch embeddings', 'Cluster and merge', 'Recluster knowledge base', 'Done']
const currentSteps = computed(() => activeTab.value === 'single' ? singleSteps : batchSteps)

const debugStats = computed(() => {
  const completed = debugEvents.value.filter(e => e.status === 'completed')
  const failed = debugEvents.value.filter(e => e.status === 'failed')
  const totalMs = completed.reduce((sum, e) => sum + (e.durationMs ?? 0), 0)
  return { completed: completed.length, failed: failed.length, totalMs }
})

function handleDebugEvent(event: Event) {
  if (!isIngesting.value || !debugEnabled.value) return
  debugEvents.value.push((event as CustomEvent<LlmDebugEvent>).detail)
}

window.addEventListener('ingest-debug-event', handleDebugEvent)
onBeforeUnmount(() => window.removeEventListener('ingest-debug-event', handleDebugEvent))

async function startIngest() {
  if (activeTab.value === 'single') {
    if (!ingestContent.value.trim()) {
      alert('請輸入要導入的內容。')
      return
    }
    await runSingleIngest()
    return
  }

  if (batchFiles.value.length === 0) {
    alert('請先加入批次檔案。')
    return
  }
  await runBatchIngest()
}

function beginRun() {
  isIngesting.value = true
  showIngestSuccess.value = false
  lastError.value = ''
  ingestStep.value = 0
  debugEvents.value = []
  expandedPayloads.value = {}

  return window.setInterval(() => {
    if (ingestStep.value < currentSteps.value.length - 2) {
      ingestStep.value++
    }
  }, 1200)
}

function finishRun(timer: number, result: IngestDebugResult | null) {
  window.clearInterval(timer)
  if (result?.events?.length) {
    debugEvents.value = mergeDebugEvents(debugEvents.value, result.events)
  }
  ingestStep.value = currentSteps.value.length - 1
  window.setTimeout(() => {
    isIngesting.value = false
    showIngestSuccess.value = true
  }, 500)
}

function failRun(timer: number, err: unknown) {
  window.clearInterval(timer)
  isIngesting.value = false
  lastError.value = err instanceof Error ? err.message : String(err)
}

async function runSingleIngest() {
  const timer = beginRun()
  try {
    const source = ingestSource.value.trim() || 'manual input'
    const result = await ipc.ingest(ingestContent.value, source, debugEnabled.value)
    finishRun(timer, result)
    ingestSource.value = ''
    ingestContent.value = ''
  } catch (err) {
    failRun(timer, err)
  }
}

async function runBatchIngest() {
  const timer = beginRun()
  try {
    const items = batchFiles.value.map(file => ({ content: file.content, source: file.name }))
    const result = await ipc.ingestBatch(items, debugEnabled.value)
    finishRun(timer, result)
    batchFiles.value = []
  } catch (err) {
    failRun(timer, err)
  }
}

function mergeDebugEvents(current: LlmDebugEvent[], incoming: LlmDebugEvent[]) {
  const seen = new Set(current.map(e => `${e.id}:${e.status}`))
  const merged = [...current]
  for (const item of incoming) {
    const key = `${item.id}:${item.status}`
    if (!seen.has(key)) {
      merged.push(item)
      seen.add(key)
    }
  }
  return merged
}

function handleFileChange(e: Event) {
  const files = (e.target as HTMLInputElement).files
  if (files) readAndAddFiles(files)
}

function handleDrop(e: DragEvent) {
  dragOver.value = false
  const files = e.dataTransfer?.files
  if (!files || files.length === 0) return

  if (activeTab.value === 'single') {
    const file = files[0]
    if (!isSupportedFile(file)) {
      alert('只支援文字檔案，例如 .txt, .md, .json, .cs, .js, .ts。')
      return
    }
    ingestSource.value = file.name
    const reader = new FileReader()
    reader.onload = event => {
      ingestContent.value = String(event.target?.result ?? '')
    }
    reader.readAsText(file)
    return
  }

  readAndAddFiles(files)
}

function readAndAddFiles(files: FileList) {
  for (let i = 0; i < files.length; i++) {
    const file = files[i]
    if (!isSupportedFile(file)) {
      alert(`不支援 ${file.name}，請使用文字檔案。`)
      continue
    }
    if (batchFiles.value.some(item => item.name === file.name)) continue

    const reader = new FileReader()
    reader.onload = event => {
      batchFiles.value.push({ name: file.name, content: String(event.target?.result ?? ''), size: file.size })
    }
    reader.readAsText(file)
  }
}

function isSupportedFile(file: File) {
  return file.type.startsWith('text/') || /\.(txt|md|json|cs|js|ts)$/i.test(file.name)
}

function removeFile(index: number) {
  batchFiles.value.splice(index, 1)
}

function clearAllFiles() {
  batchFiles.value = []
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const kb = bytes / 1024
  if (kb < 1024) return `${kb.toFixed(1)} KB`
  return `${(kb / 1024).toFixed(1)} MB`
}

function formatDuration(ms?: number): string {
  if (ms == null) return '-'
  if (ms < 1000) return `${ms} ms`
  return `${(ms / 1000).toFixed(1)} s`
}
</script>

<template>
  <div class="h-full overflow-y-auto p-8">
    <div class="mx-auto grid w-full max-w-6xl grid-cols-1 gap-5 xl:grid-cols-[minmax(0,1fr)_420px]">
      <section class="space-y-4">
        <div class="flex border-b border-white/5 gap-1">
          <button
            @click="activeTab = 'single'"
            :disabled="isIngesting"
            :class="['px-4 py-2.5 text-xs font-semibold border-b-2 transition', activeTab === 'single' ? 'border-sky-500 text-white' : 'border-transparent text-[#828b9a] hover:text-white']"
          >
            Single
          </button>
          <button
            @click="activeTab = 'batch'"
            :disabled="isIngesting"
            :class="['px-4 py-2.5 text-xs font-semibold border-b-2 transition', activeTab === 'batch' ? 'border-sky-500 text-white' : 'border-transparent text-[#828b9a] hover:text-white']"
          >
            Batch
          </button>
        </div>

        <div class="rounded-lg border border-white/5 bg-[#11141a]/40 p-5 space-y-4">
          <template v-if="activeTab === 'single'">
            <label class="block space-y-1">
              <span class="text-[10px] font-medium uppercase tracking-wide text-[#828b9a]">Source</span>
              <input
                v-model="ingestSource"
                :disabled="isIngesting"
                class="w-full rounded border border-white/5 bg-[#121620] px-3 py-2 text-xs text-white placeholder-[#828b9a] outline-none transition focus:border-sky-500"
                placeholder="meeting-notes.md"
              />
            </label>

            <label class="block space-y-1">
              <span class="text-[10px] font-medium uppercase tracking-wide text-[#828b9a]">Content</span>
              <div
                @dragover.prevent="dragOver = true"
                @dragleave.prevent="dragOver = false"
                @drop.prevent="handleDrop"
                :class="['border border-dashed rounded transition', dragOver ? 'border-sky-500 bg-sky-500/5' : 'border-white/5 bg-[#121620]/30']"
              >
                <textarea
                  v-model="ingestContent"
                  :disabled="isIngesting"
                  rows="14"
                  class="w-full resize-y bg-transparent p-3 text-xs text-slate-200 placeholder-[#828b9a] outline-none"
                  placeholder="Paste content or drop a text file..."
                />
              </div>
            </label>
          </template>

          <template v-else>
            <div
              @dragover.prevent="dragOver = true"
              @dragleave.prevent="dragOver = false"
              @drop.prevent="handleDrop"
              @click="!isIngesting && ($refs.fileInput as HTMLInputElement).click()"
              :class="['cursor-pointer rounded-lg border-2 border-dashed px-4 py-8 text-center transition', dragOver ? 'border-sky-500 bg-sky-500/5' : 'border-white/5 bg-[#121620]/30 hover:border-white/10']"
            >
              <input ref="fileInput" type="file" multiple accept=".txt,.md,.json,.cs,.js,.ts" class="hidden" @change="handleFileChange" />
              <p class="text-xs font-semibold text-white">Drop files here or click to select</p>
              <p class="mt-1 text-[10px] text-[#828b9a]">txt, md, json, cs, js, ts</p>
            </div>

            <div v-if="batchFiles.length > 0" class="space-y-2">
              <div class="flex items-center justify-between px-1">
                <span class="text-[10px] font-semibold uppercase tracking-wider text-[#828b9a]">Files ({{ batchFiles.length }})</span>
                <button :disabled="isIngesting" class="text-[10px] text-rose-400 hover:text-rose-300" @click="clearAllFiles">Clear</button>
              </div>
              <div class="max-h-56 divide-y divide-white/5 overflow-y-auto rounded border border-white/5 bg-[#121620]/60">
                <div v-for="(file, index) in batchFiles" :key="file.name" class="flex items-center justify-between gap-3 p-2.5">
                  <span class="min-w-0 truncate text-xs font-medium text-slate-200">{{ file.name }}</span>
                  <div class="flex shrink-0 items-center gap-3">
                    <span class="font-mono text-[10px] text-[#828b9a]">{{ formatSize(file.size) }}</span>
                    <button :disabled="isIngesting" class="text-[#828b9a] hover:text-rose-400" @click="removeFile(index)">Remove</button>
                  </div>
                </div>
              </div>
            </div>
          </template>

          <label class="flex items-center justify-between rounded border border-white/5 bg-[#121620]/50 px-3 py-2">
            <span>
              <span class="block text-xs font-semibold text-white">LLM Debug</span>
              <span class="block text-[10px] text-[#828b9a]">Show every chat and embedding call while ingestion runs.</span>
            </span>
            <input v-model="debugEnabled" :disabled="isIngesting" type="checkbox" class="h-4 w-4 accent-sky-500" />
          </label>

          <button
            @click="startIngest"
            :disabled="isIngesting || (activeTab === 'single' ? !ingestContent.trim() : batchFiles.length === 0)"
            class="flex w-full items-center justify-center rounded bg-sky-600 py-2.5 text-xs font-semibold text-white transition hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {{ isIngesting ? 'Ingesting...' : 'Start Ingestion' }}
          </button>
        </div>

        <div v-if="isIngesting" class="rounded-lg border border-white/5 bg-[#11141a]/40 p-5 space-y-4">
          <div class="flex items-center justify-between">
            <h3 class="text-[10px] font-bold uppercase tracking-widest text-sky-400">Pipeline</h3>
            <span class="font-mono text-[10px] text-[#828b9a]">{{ Math.round(((ingestStep + 1) / currentSteps.length) * 100) }}%</span>
          </div>
          <div class="h-1 w-full overflow-hidden rounded-full bg-white/5">
            <div class="h-full bg-sky-500 transition-all" :style="{ width: `${((ingestStep + 1) / currentSteps.length) * 100}%` }"></div>
          </div>
          <div class="space-y-2">
            <div v-for="(step, index) in currentSteps" :key="step" :class="['text-[11px]', index === ingestStep ? 'text-white' : index < ingestStep ? 'text-emerald-400' : 'text-[#828b9a] opacity-50']">
              {{ step }}
            </div>
          </div>
        </div>

        <div v-if="lastError" class="rounded-lg border border-rose-500/20 bg-rose-500/5 p-4 text-xs text-rose-200">
          {{ lastError }}
        </div>

        <div v-if="showIngestSuccess" class="rounded-lg border border-emerald-500/15 bg-emerald-500/5 p-4 text-xs text-emerald-200">
          Ingestion completed.
        </div>
      </section>

      <aside class="rounded-lg border border-white/5 bg-[#11141a]/40 p-5">
        <div class="mb-4 flex items-start justify-between gap-3">
          <div>
            <h2 class="text-xs font-bold uppercase tracking-widest text-white">LLM Debug Trace</h2>
            <p class="mt-1 text-[10px] text-[#828b9a]">Chat and embedding calls for the current or last ingestion.</p>
          </div>
          <button class="text-[10px] text-[#828b9a] hover:text-white" @click="debugEvents = []; expandedPayloads = {}">Clear</button>
        </div>

        <div class="mb-4 grid grid-cols-3 gap-2">
          <div class="rounded border border-white/5 bg-[#121620]/60 p-2">
            <div class="text-[9px] uppercase text-[#828b9a]">Events</div>
            <div class="font-mono text-sm text-white">{{ debugEvents.length }}</div>
          </div>
          <div class="rounded border border-white/5 bg-[#121620]/60 p-2">
            <div class="text-[9px] uppercase text-[#828b9a]">Failed</div>
            <div class="font-mono text-sm" :class="debugStats.failed ? 'text-rose-300' : 'text-white'">{{ debugStats.failed }}</div>
          </div>
          <div class="rounded border border-white/5 bg-[#121620]/60 p-2">
            <div class="text-[9px] uppercase text-[#828b9a]">LLM Time</div>
            <div class="font-mono text-sm text-white">{{ formatDuration(debugStats.totalMs) }}</div>
          </div>
        </div>

        <div v-if="debugEvents.length === 0" class="rounded border border-dashed border-white/5 p-6 text-center text-[11px] text-[#828b9a]">
          No debug events yet.
        </div>

        <div v-else class="max-h-[620px] space-y-2 overflow-y-auto pr-1">
          <div v-for="(event, index) in debugEvents" :key="`${event.id}-${event.status}-${index}`" class="rounded border border-white/5 bg-[#121620]/60 p-3">
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <div class="flex items-center gap-2">
                  <span :class="['h-2 w-2 rounded-full', event.status === 'failed' ? 'bg-rose-400' : event.status === 'completed' ? 'bg-emerald-400' : 'bg-sky-400']"></span>
                  <span class="truncate text-xs font-semibold text-white">{{ event.operation }}</span>
                </div>
                <div class="mt-1 text-[10px] text-[#828b9a]">{{ event.kind }} · {{ event.model || 'unknown model' }}</div>
              </div>
              <span class="shrink-0 font-mono text-[10px] text-[#828b9a]">{{ formatDuration(event.durationMs) }}</span>
            </div>

            <div class="mt-3 grid grid-cols-2 gap-2 text-[10px]">
              <div class="text-[#828b9a]">Input chars <span class="font-mono text-slate-200">{{ event.inputChars ?? '-' }}</span></div>
              <div class="text-[#828b9a]">Response <span class="font-mono text-slate-200">{{ event.responseChars ?? '-' }}</span></div>
              <div class="col-span-2 truncate text-[#828b9a]">Endpoint <span class="font-mono text-slate-200">{{ event.endpoint ?? '-' }}</span></div>
            </div>

             <p v-if="event.preview" class="mt-3 rounded bg-black/20 p-2 text-[10px] leading-relaxed text-slate-300">{{ event.preview }}</p>
            <p v-if="event.error" class="mt-3 rounded bg-rose-500/10 p-2 text-[10px] leading-relaxed text-rose-200">{{ event.error }}</p>
            
            <div v-if="event.requestPayload" class="mt-3">
              <button 
                @click="togglePayload(event.id)" 
                class="text-[10px] font-semibold text-sky-400 hover:text-sky-300 transition-colors flex items-center gap-1 focus:outline-none"
              >
                <span>{{ expandedPayloads[event.id] ? 'Hide Payload' : 'Show Payload' }}</span>
              </button>
              <pre v-if="expandedPayloads[event.id]" class="mt-2 max-h-40 overflow-y-auto rounded bg-black/40 p-2 text-[10px] font-mono text-slate-300 whitespace-pre-wrap break-all border border-white/5">{{ formatJson(event.requestPayload) }}</pre>
            </div>
          </div>
        </div>
      </aside>
    </div>
  </div>
</template>
