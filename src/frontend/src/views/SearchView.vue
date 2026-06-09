<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import MarkdownViewer from '../components/MarkdownViewer.vue'
import { ipc } from '../ipc/bridge'

type SearchResult = { entryId: string; title: string; score: number }
type Entry = { entryId: string; title: string; content: string; version: number; updatedAt: string }
type EntryVersion = { version: number; contentSnapshot: string; archivedAt: string }

const searchQuery = ref('')
const searchResults = ref<SearchResult[]>([])
const selectedEntryId = ref<string | null>(null)
const selectedEntry = ref<Entry | null>(null)
const selectedEntryHistory = ref<EntryVersion[]>([])
const isSearching = ref(false)
const showHistory = ref(false)

onMounted(() => {
  void handleSearch()
})

watch(searchQuery, (_value, _oldValue, onCleanup) => {
  const handler = window.setTimeout(() => {
    void handleSearch()
  }, 300)
  onCleanup(() => window.clearTimeout(handler))
})

async function handleSearch() {
  isSearching.value = true
  try {
    searchResults.value = await ipc.search(searchQuery.value) || []
  } catch (err) {
    console.error('Search failed:', err)
  } finally {
    isSearching.value = false
  }
}

async function selectEntry(entryId: string) {
  selectedEntryId.value = entryId
  showHistory.value = false
  try {
    selectedEntry.value = await ipc.entry.get(entryId)
    selectedEntryHistory.value = await ipc.entry.history(entryId) || []
  } catch (err) {
    console.error('Failed to load entry details:', err)
  }
}

async function handleRollback(version: number) {
  if (!selectedEntry.value) return
  if (!confirm(`確定要將此條目還原到版本 v${version} 嗎？此動作會建立新版本存檔。`)) return

  try {
    await ipc.entry.rollback(selectedEntry.value.entryId, version)
    await selectEntry(selectedEntry.value.entryId)
  } catch (err: any) {
    alert(`還原失敗: ${err.message}`)
  }
}

function formatTime(isoString: string): string {
  try {
    return new Date(isoString).toLocaleString('zh-TW', { hour12: false })
  } catch {
    return isoString
  }
}
</script>

<template>
  <div class="h-full flex overflow-hidden">
    <div class="w-[35%] border-r border-white/5 flex flex-col h-full bg-[#0a0c10] shrink-0">
      <div class="p-4 border-b border-white/5">
        <div class="relative">
          <input
            v-model="searchQuery"
            type="text"
            placeholder="輸入關鍵字或語意問題..."
            class="w-full bg-[#121620] border border-white/5 rounded py-2 pl-9 pr-4 text-xs text-white placeholder-[#828b9a] focus:outline-none focus:border-sky-500 transition duration-150"
          />
          <span class="absolute left-3 top-3 text-[#828b9a]">
            <svg v-if="!isSearching" class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <svg v-else class="w-3.5 h-3.5 animate-spin text-sky-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
            </svg>
          </span>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto p-3 space-y-2">
        <div v-if="searchResults.length === 0" class="text-center py-12 text-xs text-[#828b9a]">
          尚無搜尋結果
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
          <div class="absolute left-0 bottom-0 h-[1.5px] bg-sky-500 transition-all duration-300" :style="{ width: `${item.score * 100}%` }"></div>
          <div class="flex justify-between items-start gap-2">
            <span class="font-medium text-xs text-white group-hover:text-sky-400 transition-colors line-clamp-2">{{ item.title }}</span>
            <span class="text-[10px] text-sky-400 font-mono shrink-0">{{ Math.round(item.score * 100) }}% Match</span>
          </div>
          <div class="text-[10px] text-[#828b9a] font-mono">ID: {{ item.entryId.substring(0, 8) }}</div>
        </button>
      </div>
    </div>

    <div class="flex-1 flex flex-col h-full bg-[#090b0f] overflow-hidden">
      <div v-if="!selectedEntry" class="flex-1 flex flex-col items-center justify-center text-center p-8">
        <div class="w-12 h-12 rounded bg-white/[0.015] border border-white/5 flex items-center justify-center text-[#828b9a] mb-4">
          <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
          </svg>
        </div>
        <h3 class="text-xs font-semibold text-white mb-1">選取一筆知識條目</h3>
        <p class="text-[11px] text-[#828b9a] max-w-xs">從左側搜尋結果開啟內容與版本歷史。</p>
      </div>

      <div v-else class="flex-1 flex flex-col min-h-0">
        <div class="px-6 py-4 border-b border-white/5 flex items-start justify-between gap-4">
          <div>
            <h2 class="text-sm font-semibold text-white">{{ selectedEntry.title }}</h2>
            <p class="text-[10px] text-[#828b9a] mt-1">
              v{{ selectedEntry.version }} · {{ formatTime(selectedEntry.updatedAt) }}
            </p>
          </div>
          <button
            @click="showHistory = !showHistory"
            class="bg-white/[0.015] border border-white/5 hover:bg-white/[0.03] text-white rounded px-3 py-1.5 text-[11px] font-medium transition"
          >
            {{ showHistory ? '關閉版本' : '版本歷史' }}
          </button>
        </div>

        <div class="flex-1 flex min-h-0">
          <div class="flex-1 overflow-y-auto p-6 prose max-w-none text-slate-300">
            <MarkdownViewer :content="selectedEntry.content" />
          </div>

          <aside v-if="showHistory" class="w-72 border-l border-white/5 bg-[#0a0c10] overflow-y-auto p-4 space-y-3">
            <h3 class="text-[10px] text-[#828b9a] font-semibold tracking-widest uppercase">Version History</h3>
            <div v-if="selectedEntryHistory.length === 0" class="text-[11px] text-[#828b9a]">尚無歷史版本</div>
            <div v-for="version in selectedEntryHistory" :key="version.version" class="bg-[#11141a]/40 border border-white/5 rounded p-3 space-y-2">
              <div class="flex justify-between items-center font-mono text-[10px]">
                <span class="font-bold text-white">v{{ version.version }}</span>
                <span class="text-[#828b9a]">{{ formatTime(version.archivedAt) }}</span>
              </div>
              <p class="text-[10px] text-[#828b9a] line-clamp-3 italic leading-relaxed">{{ version.contentSnapshot }}</p>
              <button
                @click="handleRollback(version.version)"
                class="w-full text-center bg-sky-500/10 hover:bg-sky-500/20 text-sky-400 border border-sky-500/20 rounded py-1 text-[11px] font-medium transition"
              >
                還原此版本
              </button>
            </div>
          </aside>
        </div>
      </div>
    </div>
  </div>
</template>
