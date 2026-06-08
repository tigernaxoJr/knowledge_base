<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import { ipc } from './ipc/bridge'

// ── 應用狀態 ────────────────────────────────────────────────────────────────
const currentTab = ref<'search' | 'ingest' | 'config'>('search')

// ── 1. 檢索狀態 ────────────────────────────────────────────────────────────
const searchQuery = ref('')
const searchResults = ref<Array<{ entryId: string; title: string; score: number }>>([])
const selectedEntryId = ref<string | null>(null)
const selectedEntry = ref<{ entryId: string; title: string; content: string; version: number; updatedAt: string } | null>(null)
const selectedEntryHistory = ref<Array<{ version: number; contentSnapshot: string; archivedAt: string }>>([])
const isSearching = ref(false)
const showHistory = ref(false)

// ── 2. 導入狀態 ────────────────────────────────────────────────────────────
const ingestSource = ref('')
const ingestContent = ref('')
const isIngesting = ref(false)
const ingestStep = ref(0)
const showIngestSuccess = ref(false)
const dragOver = ref(false)

const ingestSteps = [
  '正在分析文件格式...',
  'LLM 正在提煉去噪大綱（限制於 400 字）...',
  '正在計算大綱 Embedding 向量...',
  '正在比對既有知識庫條目（相似度閾值 0.82）...',
  '進行增量知識融合與版本備份中...',
  '寫入資料庫與更新向量索引...'
]

// ── 3. 設定狀態 ────────────────────────────────────────────────────────────
const config = ref({
  llmConfig: { endpoint: '', apiKey: '', modelName: '' },
  embeddingConfig: { endpoint: '', apiKey: '', modelName: '' }
})
const showLlmKey = ref(false)
const showEmbedKey = ref(false)
const isSavingConfig = ref(false)
const testStatus = ref<'idle' | 'testing' | 'success' | 'failed'>('idle')
const testErrorMessage = ref<string | null>(null)

// ── 生命週期與初始化 ─────────────────────────────────────────────────────────
onMounted(async () => {
  await loadConfig()
  await handleSearch() // 初始載入熱門知識
})

// ── 函數：設定管理 ──────────────────────────────────────────────────────────
async function loadConfig() {
  try {
    const data = await ipc.config.load()
    if (data) {
      config.value.llmConfig = data.llmConfig || { endpoint: '', apiKey: '', modelName: '' }
      config.value.embeddingConfig = data.embeddingConfig || { endpoint: '', apiKey: '', modelName: '' }
    }
  } catch (err) {
    console.error('Failed to load config:', err)
  }
}

async function saveConfig() {
  isSavingConfig.value = true
  try {
    await ipc.config.save(config.value)
    alert('設定已成功儲存！')
  } catch (err: any) {
    alert(`儲存設定失敗: ${err.message}`)
  } finally {
    isSavingConfig.value = false
  }
}

async function testConnection() {
  testStatus.value = 'testing'
  testErrorMessage.value = null
  try {
    const res = await ipc.config.test(
      config.value.llmConfig.endpoint,
      config.value.llmConfig.apiKey,
      config.value.llmConfig.modelName
    )
    if (res.success) {
      testStatus.value = 'success'
    } else {
      testStatus.value = 'failed'
      testErrorMessage.value = res.errorMessage || '連線測試失敗。'
    }
  } catch (err: any) {
    testStatus.value = 'failed'
    testErrorMessage.value = err.message
  }
}

// ── 函數：知識檢索 ──────────────────────────────────────────────────────────
async function handleSearch() {
  isSearching.value = true
  try {
    const results = await ipc.search(searchQuery.value)
    searchResults.value = results || []
  } catch (err) {
    console.error('Search failed:', err)
  } finally {
    isSearching.value = false
  }
}

// 防抖搜尋
watch(searchQuery, () => {
  const handler = setTimeout(() => {
    handleSearch()
  }, 300)
  return () => clearTimeout(handler)
})

async function selectEntry(entryId: string) {
  selectedEntryId.value = entryId
  showHistory.value = false
  try {
    selectedEntry.value = await ipc.entry.get(entryId)
    // 載入該條目的修改歷史
    const history = await ipc.entry.history(entryId)
    selectedEntryHistory.value = history || []
  } catch (err) {
    console.error('Failed to load entry details:', err)
  }
}

async function handleRollback(version: number) {
  if (!selectedEntry.value) return
  if (!confirm(`確定要將此條目還原到版本 v${version} 嗎？此動作會建立新版本存檔。`)) return

  try {
    await ipc.entry.rollback(selectedEntry.value.entryId, version)
    alert(`已成功還原至版本 v${version}！`)
    // 重新載入條目
    await selectEntry(selectedEntry.value.entryId)
  } catch (err: any) {
    alert(`還原失敗: ${err.message}`)
  }
}

// ── 函數：文件導入 ──────────────────────────────────────────────────────────
async function startIngest() {
  if (!ingestContent.value.trim()) {
    alert('請輸入或拖放文件內容！')
    return
  }

  isIngesting.value = true
  showIngestSuccess.value = false
  ingestStep.value = 0

  // 模擬進度步驟動畫，提升視覺體驗與互動感
  const stepTimer = setInterval(() => {
    if (ingestStep.value < ingestSteps.length - 2) {
      ingestStep.value++
    }
  }, 1000)

  try {
    const source = ingestSource.value.trim() || '手動導入'
    await ipc.ingest(ingestContent.value, source)
    
    clearInterval(stepTimer)
    ingestStep.value = ingestSteps.length - 1
    
    setTimeout(() => {
      isIngesting.value = false
      showIngestSuccess.value = true
      ingestSource.value = ''
      ingestContent.value = ''
      handleSearch() // 刷新搜尋結果
    }, 800)
  } catch (err: any) {
    clearInterval(stepTimer)
    isIngesting.value = false
    alert(`文件導入失敗: ${err.message}`)
  }
}

// 拖放檔案處理
function handleDrop(e: DragEvent) {
  dragOver.value = false
  const files = e.dataTransfer?.files
  if (!files || files.length === 0) return

  const file = files[0]
  if (file.type.startsWith('text/') || file.name.endsWith('.txt') || file.name.endsWith('.md') || file.name.endsWith('.json')) {
    ingestSource.value = file.name
    const reader = new FileReader()
    reader.onload = (event) => {
      ingestContent.value = event.target?.result as string || ''
    }
    reader.readAsText(file)
  } else {
    alert('僅支援純文字格式檔案（.txt, .md, .json 等）！')
  }
}

// ── 輔助方法：Markdown 渲染器 ────────────────────────────────────────────────
const renderedContent = computed(() => {
  if (!selectedEntry.value) return ''
  return parseMarkdown(selectedEntry.value.content)
})

function parseMarkdown(md: string): string {
  if (!md) return ''
  let html = md
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');

  // 程式碼區塊 (```lang ... ```)
  html = html.replace(/```([\s\S]*?)```/g, (_, code) => {
    return `<pre class="bg-black/35 p-4 rounded border border-white/5 overflow-x-auto my-4 font-mono text-xs text-sky-400"><code>${code.trim()}</code></pre>`;
  });

  // 行內程式碼 (`code`)
  html = html.replace(/`([^`]+)`/g, '<code class="bg-black/25 px-1.5 py-0.5 rounded text-sky-400 font-mono text-xs border border-white/5">$1</code>');

  // 標題 (#, ##, ###)
  html = html.replace(/^### (.*$)/gim, '<h3 class="text-xs font-semibold text-white mt-4 mb-2">$1</h3>');
  html = html.replace(/^## (.*$)/gim, '<h2 class="text-sm font-semibold text-white mt-6 mb-3 border-b border-white/5 pb-1.5">$1</h2>');
  html = html.replace(/^# (.*$)/gim, '<h1 class="text-base font-bold text-white mt-8 mb-4 border-b border-white/10 pb-2">$1</h1>');

  // 粗體 (**text**)
  html = html.replace(/\*\*([^*]+)\*\*/g, '<strong class="font-semibold text-sky-400">$1</strong>');

  // 無序清單 (- list)
  html = html.replace(/^\s*-\s+(.*$)/gim, '<li class="list-disc ml-5 mb-1.5 text-slate-300">$1</li>');

  // 換行與段落處理
  const lines = html.split('\n');
  const processedLines = lines.map(line => {
    const trimmed = line.trim();
    if (!trimmed) return '';
    if (trimmed.startsWith('<h') || trimmed.startsWith('<pre') || trimmed.startsWith('<code') || trimmed.startsWith('<li') || trimmed.startsWith('</pre>') || trimmed.startsWith('</code') || trimmed.startsWith('---')) {
      return line;
    }
    return `<p class="mb-3 text-slate-300 leading-relaxed">${line}</p>`;
  });

  return processedLines.join('\n');
}

function formatTime(isoString: string): string {
  try {
    const date = new Date(isoString)
    return date.toLocaleString('zh-TW', { hour12: false })
  } catch {
    return isoString
  }
}
</script>

<template>
  <div class="flex h-screen bg-[#090b0f] text-[#f3f4f6] font-sans selection:bg-sky-500/20 overflow-hidden">
    
    <!-- ── 側邊導航欄 (Sidebar) ── -->
    <aside class="w-60 bg-[#0d0f14] border-r border-white/5 flex flex-col justify-between shrink-0">
      <div>
        <!-- App 標題 -->
        <div class="p-5 border-b border-white/5 flex items-center gap-2.5">
          <div class="w-7 h-7 rounded bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-sky-400">
            <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M9.663 17h4.673M12 3v1m6.364.364l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
            </svg>
          </div>
          <span class="font-bold text-xs tracking-widest text-white uppercase">KnowledgeOS</span>
        </div>

        <!-- 導航選單 -->
        <nav class="p-3 space-y-1">
          <button
            @click="currentTab = 'search'"
            :class="['w-full flex items-center gap-3 px-3 py-2.5 rounded text-xs font-medium transition duration-150 border-l-2', 
              currentTab === 'search' 
                ? 'bg-white/[0.03] border-sky-500 text-sky-400' 
                : 'border-transparent text-[#828b9a] hover:bg-white/[0.015] hover:text-white']"
          >
            <svg class="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            知識檢索
          </button>
          
          <button
            @click="currentTab = 'ingest'"
            :class="['w-full flex items-center gap-3 px-3 py-2.5 rounded text-xs font-medium transition duration-150 border-l-2', 
              currentTab === 'ingest' 
                ? 'bg-white/[0.03] border-sky-500 text-sky-400' 
                : 'border-transparent text-[#828b9a] hover:bg-white/[0.015] hover:text-white']"
          >
            <svg class="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M9 13h6m-3-3v6m5 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
            文件導入
          </button>
          
          <button
            @click="currentTab = 'config'"
            :class="['w-full flex items-center gap-3 px-3 py-2.5 rounded text-xs font-medium transition duration-150 border-l-2', 
              currentTab === 'config' 
                ? 'bg-white/[0.03] border-sky-500 text-sky-400' 
                : 'border-transparent text-[#828b9a] hover:bg-white/[0.015] hover:text-white']"
          >
            <svg class="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
              <path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
            設定中心
          </button>
        </nav>
      </div>

      <!-- 側邊欄底部資訊 -->
      <div class="p-4 border-t border-white/5 text-[10px] text-[#828b9a]">
        <div class="flex items-center gap-2">
          <span class="w-1.5 h-1.5 rounded-full bg-sky-500"></span>
          <span>離線運作模式</span>
        </div>
      </div>
    </aside>

    <!-- ── 主內容區域 (Main Frame) ── -->
    <main class="flex-1 flex flex-col min-w-0 bg-[#090b0f]">
      
      <!-- 頂部 Header -->
      <header class="h-14 border-b border-white/5 bg-[#0d0f14]/50 backdrop-blur-md flex items-center justify-between px-6 shrink-0 z-10">
        <h1 class="text-xs font-semibold text-white tracking-widest uppercase">
          <template v-if="currentTab === 'search'">語意向量檢索 / RAG Query</template>
          <template v-if="currentTab === 'ingest'">大綱提煉與文件導入 / Knowledge Ingestion</template>
          <template v-if="currentTab === 'config'">本地連線組態設定 / Configuration</template>
        </h1>
      </header>

      <!-- 內容視圖交換 -->
      <div class="flex-1 overflow-hidden">
        
        <!-- 1. 檢索視圖 (Search Tab) -->
        <div v-if="currentTab === 'search'" class="h-full flex overflow-hidden">
          
          <!-- 左側：搜尋輸入與結果清單 (Width: 2/5) -->
          <div class="w-[35%] border-r border-white/5 flex flex-col h-full bg-[#0a0c10] shrink-0">
            <!-- 搜尋框 -->
            <div class="p-4 border-b border-white/5">
              <div class="relative">
                <input
                  v-model="searchQuery"
                  type="text"
                  placeholder="輸入關鍵字進行語意搜尋..."
                  class="w-full bg-[#121620] border border-white/5 rounded py-2 pl-9 pr-4 text-xs text-white placeholder-[#828b9a] focus:outline-none focus:border-sky-500 transition duration-150"
                />
                <span class="absolute left-3 top-3 text-[#828b9a]">
                  <svg v-if="!isSearching" class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                  </svg>
                  <!-- 旋轉 Loading -->
                  <svg v-else class="w-3.5 h-3.5 animate-spin text-sky-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                </span>
              </div>
            </div>

            <!-- 結果清單 -->
            <div class="flex-1 overflow-y-auto p-3 space-y-2">
              <div v-if="searchResults.length === 0" class="text-center py-12 text-xs text-[#828b9a]">
                無匹配的知識條目。請先在「文件導入」頁面新增資料。
              </div>
              <button
                v-for="item in searchResults"
                :key="item.entryId"
                @click="selectEntry(item.entryId)"
                :class="['w-full text-left p-3.5 rounded border transition-all duration-150 group flex flex-col gap-2 relative overflow-hidden',
                  selectedEntryId === item.entryId 
                    ? 'bg-white/[0.02] border-sky-500' 
                    : 'bg-transparent border-transparent hover:bg-white/[0.015] hover:border-white/5']"
              >
                <!-- 相似度比對進度條背景 -->
                <div 
                  class="absolute left-0 bottom-0 h-[1.5px] bg-sky-500 transition-all duration-300"
                  :style="{ width: `${item.score * 100}%` }"
                ></div>

                <div class="flex justify-between items-start gap-2">
                  <span class="font-medium text-xs text-white group-hover:text-sky-400 transition-colors line-clamp-2">{{ item.title }}</span>
                  <span class="text-[10px] text-sky-400 font-mono shrink-0">
                    {{ Math.round(item.score * 100) }}% Match
                  </span>
                </div>
                <div class="text-[10px] text-[#828b9a] font-mono">
                  ID: {{ item.entryId.substring(0, 8) }}
                </div>
              </button>
            </div>
          </div>

          <!-- 右側：條目全文詳細資訊 (Width: 3/5) -->
          <div class="flex-1 flex flex-col h-full bg-[#090b0f] overflow-hidden">
            
            <!-- 未選取條目 Placeholder -->
            <div v-if="!selectedEntry" class="flex-1 flex flex-col items-center justify-center text-center p-8">
              <div class="w-12 h-12 rounded bg-white/[0.015] border border-white/5 flex items-center justify-center text-[#828b9a] mb-4">
                <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
                </svg>
              </div>
              <h3 class="text-xs font-semibold text-white mb-1">未選取知識條目</h3>
              <p class="text-[11px] text-[#828b9a] max-w-xs">請選擇左側的搜尋結果以檢視結構化 Markdown 全文與版本歷程。</p>
            </div>

            <!-- 條目詳情視圖 -->
            <div v-else class="flex-1 flex overflow-hidden">
              
              <!-- 條目本文 -->
              <div class="flex-1 flex flex-col h-full min-w-0">
                <!-- 條目 Header -->
                <div class="p-5 border-b border-white/5 bg-[#0d0f14]/30 flex justify-between items-center shrink-0">
                  <div class="min-w-0">
                    <h2 class="text-xs font-bold text-white truncate mb-1.5">{{ selectedEntry.title }}</h2>
                    <div class="flex items-center gap-3 text-[10px] text-[#828b9a] font-mono">
                      <span class="bg-sky-500/10 text-sky-400 border border-sky-500/20 px-1.5 py-0.5 rounded font-bold">Version v{{ selectedEntry.version }}</span>
                      <span>Update: {{ formatTime(selectedEntry.updatedAt) }}</span>
                    </div>
                  </div>
                  
                  <button 
                    @click="showHistory = !showHistory"
                    :class="['flex items-center gap-1.5 px-2.5 py-1.5 rounded text-xs font-medium border transition-all duration-150',
                      showHistory 
                        ? 'bg-sky-600 border-sky-500 text-white' 
                        : 'bg-white/[0.015] border-white/5 text-[#828b9a] hover:bg-white/[0.03] hover:text-white']"
                  >
                    <svg class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    歷程版本
                  </button>
                </div>

                <!-- 條目內容渲染器 -->
                <div class="flex-1 overflow-y-auto p-6 prose max-w-none text-slate-300 markdown-body" v-html="renderedContent"></div>
              </div>

              <!-- 歷程版本抽屜 (History Panel) -->
              <div 
                v-if="showHistory" 
                class="w-72 border-l border-white/5 bg-[#0a0c10] flex flex-col h-full shrink-0 transition-all duration-150"
              >
                <div class="p-3 border-b border-white/5 flex items-center justify-between">
                  <span class="text-[10px] font-bold text-white tracking-widest uppercase">版本修訂歷史</span>
                  <button @click="showHistory = false" class="text-[#828b9a] hover:text-white">
                    <svg class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
                
                <div class="flex-1 overflow-y-auto p-3 space-y-3">
                  <div v-if="selectedEntryHistory.length === 0" class="text-center py-12 text-[11px] text-[#828b9a]">
                    無歷史版本紀錄（目前為初始版本）。
                  </div>
                  
                  <div 
                    v-for="version in selectedEntryHistory"
                    :key="version.version"
                    class="p-3 rounded border border-white/5 bg-white/[0.005] hover:bg-white/[0.015] transition flex flex-col gap-2.5"
                  >
                    <div class="flex justify-between items-center font-mono text-[10px]">
                      <span class="font-bold text-white">Version v{{ version.version }}</span>
                      <span class="text-[#828b9a]">{{ formatTime(version.archivedAt) }}</span>
                    </div>
                    
                    <p class="text-[10px] text-[#828b9a] line-clamp-3 italic leading-relaxed">
                      {{ version.contentSnapshot }}
                    </p>
                    
                    <button 
                      @click="handleRollback(version.version)"
                      class="w-full text-center bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 border border-sky-500/20 rounded py-1 text-[11px] font-medium transition"
                    >
                      還原至此版本
                    </button>
                  </div>
                </div>
              </div>

            </div>
          </div>
        </div>

        <!-- 2. 文件導入視圖 (Ingest Tab) -->
        <div v-if="currentTab === 'ingest'" class="h-full overflow-y-auto p-8 flex justify-center">
          <div class="w-full max-w-2xl space-y-4">
            
            <div class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-4">
              <h2 class="text-xs font-semibold text-white tracking-widest uppercase">導入新知識文件</h2>
              
              <!-- 來源欄位 -->
              <div class="space-y-1">
                <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">文件來源標籤（Source）</label>
                <input
                  v-model="ingestSource"
                  type="text"
                  placeholder="例如: 系統規格書 v1.0.txt"
                  class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white placeholder-[#828b9a] focus:outline-none focus:border-sky-500 transition"
                  :disabled="isIngesting"
                />
              </div>

              <!-- 拖放與編輯區域 -->
              <div class="space-y-1">
                <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">文件全文內容（Content）</label>
                
                <!-- 拖放區 -->
                <div 
                  @dragover.prevent="dragOver = true"
                  @dragleave.prevent="dragOver = false"
                  @drop.prevent="handleDrop"
                  :class="['relative border border-dashed rounded transition-all duration-150 flex flex-col',
                    dragOver 
                      ? 'border-sky-500 bg-sky-500/5' 
                      : 'border-white/5 hover:border-white/10 bg-[#121620]/30']"
                >
                  <textarea
                    v-model="ingestContent"
                    rows="10"
                    placeholder="請輸入欲導入的文字知識內容，或是將文字檔 (.txt / .md / .json) 直接拖放到此區域..."
                    class="w-full bg-transparent border-0 p-3 text-xs text-slate-200 placeholder-[#828b9a] focus:outline-none resize-y"
                    :disabled="isIngesting"
                  ></textarea>
                  
                  <div class="absolute right-3 bottom-3 text-[9px] text-[#828b9a] pointer-events-none uppercase font-mono tracking-wider">
                    Drag & Drop Ready
                  </div>
                </div>
              </div>

              <!-- 提交按鈕 -->
              <button
                @click="startIngest"
                :disabled="isIngesting || !ingestContent.trim()"
                class="w-full bg-sky-600 hover:bg-sky-700 text-white rounded py-2.5 text-xs font-semibold transition disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
              >
                <template v-if="!isIngesting">
                  <svg class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
                  </svg>
                  執行增量融合導入
                </template>
                <template v-else>
                  <svg class="w-3.5 h-3.5 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  正在處理...
                </template>
              </button>
            </div>

            <!-- Ingestion Progress Steps Loader -->
            <div v-if="isIngesting" class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-3">
              <h3 class="text-[9px] font-bold text-sky-400 tracking-widest uppercase">Ingestion Pipeline Running</h3>
              
              <div class="space-y-2.5">
                <div 
                  v-for="(step, index) in ingestSteps"
                  :key="index"
                  :class="['flex items-center gap-2.5 text-[11px] transition-opacity duration-150',
                    index === ingestStep 
                      ? 'text-white font-medium' 
                      : index < ingestStep 
                        ? 'text-emerald-400 opacity-60' 
                        : 'text-[#828b9a] opacity-30']"
                >
                  <div class="shrink-0">
                    <svg v-if="index < ingestStep" class="w-3.5 h-3.5 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
                    </svg>
                    <div v-else-if="index === ingestStep" class="w-3 h-3 rounded-full border-2 border-sky-500 border-t-transparent animate-spin"></div>
                    <div v-else class="w-1 h-1 rounded-full bg-[#828b9a] mx-1"></div>
                  </div>
                  
                  <span>{{ step }}</span>
                </div>
              </div>
            </div>

            <!-- Ingestion Success Alert Card -->
            <div v-if="showIngestSuccess" class="bg-emerald-500/5 border border-emerald-500/15 rounded p-5 flex gap-3.5 items-start">
              <div class="shrink-0 w-7 h-7 rounded bg-emerald-500/10 flex items-center justify-center text-emerald-400">
                <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <div>
                <h3 class="text-xs font-semibold text-white mb-0.5">文件導入暨增量融合完成</h3>
                <p class="text-[11px] text-[#828b9a] leading-relaxed">該文件已完成去噪大綱提煉，並成功與最相似的知識主題完成增量融合。條目已被最新資訊覆蓋，歷史版本已備份存檔。</p>
              </div>
            </div>

          </div>
        </div>

        <!-- 3. 設定中心視圖 (Config Tab) -->
        <div v-if="currentTab === 'config'" class="h-full overflow-y-auto p-8 flex justify-center">
          <div class="w-full max-w-2xl space-y-4">
            
            <!-- 大語言模型 (LLM) 設定 -->
            <div class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-4">
              <div class="flex items-center gap-2.5">
                <div class="w-6.5 h-6.5 rounded bg-sky-500/10 flex items-center justify-center text-sky-400">
                  <svg class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
                  </svg>
                </div>
                <h2 class="text-xs font-semibold text-white tracking-widest uppercase">大語言模型服務提供端點 (LLM Config)</h2>
              </div>
              
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div class="space-y-1">
                  <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Endpoint 連線網址</label>
                  <input
                    v-model="config.llmConfig.endpoint"
                    type="text"
                    placeholder="https://api.openai.com/v1"
                    class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition"
                  />
                </div>
                <div class="space-y-1">
                  <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Model 模型型號</label>
                  <input
                    v-model="config.llmConfig.modelName"
                    type="text"
                    placeholder="gpt-4o"
                    class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition"
                  />
                </div>
                <div class="space-y-1 md:col-span-2">
                  <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API 授權金鑰 (ApiKey)</label>
                  <div class="relative">
                    <input
                      v-model="config.llmConfig.apiKey"
                      :type="showLlmKey ? 'text' : 'password'"
                      placeholder="sk-xxxxxxxxxxxxxxxxxxxxxxxx"
                      class="w-full bg-[#121620] border border-white/5 rounded py-2 pl-3 pr-9 text-xs text-white focus:outline-none focus:border-sky-500 transition"
                    />
                    <button 
                      @click="showLlmKey = !showLlmKey"
                      class="absolute right-2.5 top-2 text-[#828b9a] hover:text-white"
                    >
                      <svg v-if="!showLlmKey" class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        <path stroke-linecap="round" stroke-linejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                      </svg>
                      <svg v-else class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.542-7a10.025 10.025 0 012.236-4.238m8.044-2.064m8.044 2.064A10.025 10.025 0 0121.542 12c-1.274 4.057-5.064 7-9.542 7-1.273 0-2.485-.209-3.611-.572M10.875 10.875a3 3 0 004.25 4.25m-4.25-4.25L9 9m4 4l3.875 3.875" />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <!-- 向量嵌入模型 (Embedding) 設定 -->
            <div class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-4">
              <div class="flex items-center gap-2.5">
                <div class="w-6.5 h-6.5 rounded bg-sky-500/10 flex items-center justify-center text-sky-400">
                  <svg class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M7 21a4 4 0 01-4-4V5a2 2 0 012-2h4a2 2 0 012 2v12a4 4 0 01-4 4zm0 0h12a2 2 0 002-2v-4a2 2 0 00-2-2h-2.343M11 7.343l1.657-1.657a2 2 0 012.828 0l2.829 2.829a2 2 0 010 2.828l-8.486 8.485M7 17h.01" />
                  </svg>
                </div>
                <h2 class="text-xs font-semibold text-white tracking-widest uppercase">向量嵌入服務端點 (Embedding Config)</h2>
              </div>
              
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div class="space-y-1">
                  <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Endpoint 連線網址</label>
                  <input
                    v-model="config.embeddingConfig.endpoint"
                    type="text"
                    placeholder="https://api.openai.com/v1"
                    class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition"
                  />
                </div>
                <div class="space-y-1">
                  <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Model 向量模型名稱</label>
                  <input
                    v-model="config.embeddingConfig.modelName"
                    type="text"
                    placeholder="text-embedding-3-small"
                    class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition"
                  />
                </div>
                <div class="space-y-1 md:col-span-2">
                  <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API 授權金鑰 (ApiKey)</label>
                  <div class="relative">
                    <input
                      v-model="config.embeddingConfig.apiKey"
                      :type="showEmbedKey ? 'text' : 'password'"
                      placeholder="sk-yyyyyyyyyyyyyyyyyyyyyyyy"
                      class="w-full bg-[#121620] border border-white/5 rounded py-2 pl-3 pr-9 text-xs text-white focus:outline-none focus:border-sky-500 transition"
                    />
                    <button 
                      @click="showEmbedKey = !showEmbedKey"
                      class="absolute right-2.5 top-2 text-[#828b9a] hover:text-white"
                    >
                      <svg v-if="!showEmbedKey" class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        <path stroke-linecap="round" stroke-linejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                      </svg>
                      <svg v-else class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.542-7a10.025 10.025 0 012.236-4.238m8.044-2.064m8.044 2.064A10.025 10.025 0 0121.542 12c-1.274 4.057-5.064 7-9.542 7-1.273 0-2.485-.209-3.611-.572M10.875 10.875a3 3 0 004.25 4.25m-4.25-4.25L9 9m4 4l3.875 3.875" />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <!-- 連線測試結果呈現區 -->
            <div v-if="testStatus !== 'idle'" :class="['p-4 rounded border flex gap-3.5 items-start transition', 
              testStatus === 'testing' 
                ? 'bg-white/[0.015] border-white/5 text-[#828b9a]' 
                : testStatus === 'success' 
                  ? 'bg-emerald-500/5 border-emerald-500/15 text-emerald-400' 
                  : 'bg-rose-500/5 border-rose-500/15 text-rose-400']"
            >
              <div class="shrink-0">
                <svg v-if="testStatus === 'testing'" class="w-4 h-4 animate-spin text-sky-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                <svg v-else-if="testStatus === 'success'" class="w-4 h-4 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <svg v-else class="w-4 h-4 text-rose-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>

              <div>
                <h4 class="text-xs font-bold text-white mb-0.5">
                  <template v-if="testStatus === 'testing'">正在測試 API 連線...</template>
                  <template v-else-if="testStatus === 'success'">LLM 連線成功</template>
                  <template v-else>LLM 連線失敗</template>
                </h4>
                <p class="text-[11px] leading-relaxed text-[#828b9a]">
                  <template v-if="testStatus === 'testing'">正在發送 API 請求以確認授權金鑰與端點回應速率...</template>
                  <template v-else-if="testStatus === 'success'">已成功存取大語言模型服務，憑證與模型皆正常。</template>
                  <template v-else>{{ testErrorMessage }}</template>
                </p>
              </div>
            </div>

            <!-- 控制列 (測試與儲存) -->
            <div class="flex gap-4">
              <button
                @click="testConnection"
                :disabled="testStatus === 'testing'"
                class="flex-1 bg-white/[0.015] border border-white/5 hover:bg-white/[0.03] text-white rounded py-2.5 text-xs font-semibold transition"
              >
                測試 LLM 連線
              </button>
              
              <button
                @click="saveConfig"
                :disabled="isSavingConfig"
                class="flex-1 bg-sky-600 hover:bg-sky-700 text-white rounded py-2.5 text-xs font-semibold transition"
              >
                <template v-if="!isSavingConfig">儲存設定</template>
                <template v-else>正在儲存...</template>
              </button>
            </div>

          </div>
        </div>

      </div>

    </main>
  </div>
</template>

<style>
/* ── Markdown 渲染排版客製化 ── */
.markdown-body {
  font-family: inherit;
}
.markdown-body pre code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  background: transparent !important;
  border: 0;
  padding: 0;
}
</style>

<style>
/* ── Markdown 渲染排版客製化 ── */
.markdown-body {
  font-family: inherit;
}
.markdown-body pre code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  background: transparent !important;
  border: 0;
  padding: 0;
}
</style>
