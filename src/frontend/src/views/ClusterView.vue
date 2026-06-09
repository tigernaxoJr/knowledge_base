<script setup lang="ts">
import { onMounted, ref } from 'vue'
import MarkdownViewer from '../components/MarkdownViewer.vue'
import { ipc } from '../ipc/bridge'

type ClusterEntry = { entryId: string; title: string; version: number; updatedAt: string }
type Cluster = { clusterId: string; name: string; entries: ClusterEntry[] }
type Entry = { entryId: string; title: string; content: string; version: number; updatedAt: string }

const clusters = ref<Cluster[]>([])
const selectedClusterId = ref<string | null>(null)
const selectedCluster = ref<Cluster | null>(null)

const selectedEntryId = ref<string | null>(null)
const selectedEntry = ref<Entry | null>(null)

const isLoading = ref(false)
const isReclustering = ref(false)

// Editing States
const isEditing = ref(false)
const editTitle = ref('')
const editContent = ref('')
const isSaving = ref(false)

onMounted(() => {
  void loadClusters()
})

async function loadClusters(selectFirst = false) {
  isLoading.value = true
  try {
    const data = await ipc.cluster.list()
    clusters.value = data || []
    
    if (selectFirst && clusters.value.length > 0) {
      selectCluster(clusters.value[0])
    } else if (selectedClusterId.value) {
      // Try to re-select currently selected cluster
      const current = clusters.value.find(c => c.clusterId === selectedClusterId.value)
      if (current) {
        selectCluster(current)
      }
    }
  } catch (err) {
    console.error('Failed to load clusters:', err)
  } finally {
    isLoading.value = false
  }
}

async function triggerRecluster() {
  isReclustering.value = true
  try {
    await ipc.cluster.recluster()
    await loadClusters()
    alert('知識分群已重新計算並儲存完成！')
  } catch (err: any) {
    alert(`重新分群失敗: ${err.message}`)
  } finally {
    isReclustering.value = false
  }
}

function selectCluster(cluster: Cluster) {
  selectedClusterId.value = cluster.clusterId
  selectedCluster.value = cluster
  selectedEntryId.value = null
  selectedEntry.value = null
  isEditing.value = false
}

async function selectEntry(entryId: string) {
  selectedEntryId.value = entryId
  isEditing.value = false
  try {
    selectedEntry.value = await ipc.entry.get(entryId)
  } catch (err) {
    console.error('Failed to load entry details:', err)
  }
}

function startEditing() {
  if (!selectedEntry.value) return
  editTitle.value = selectedEntry.value.title
  editContent.value = selectedEntry.value.content
  isEditing.value = true
}

function cancelEditing() {
  isEditing.value = false
}

async function handleSaveEdit() {
  if (!selectedEntry.value) return
  const title = editTitle.value.trim()
  const content = editContent.value.trim()
  if (!title) {
    alert('標題不能為空')
    return
  }
  if (!content) {
    alert('內容不能為空')
    return
  }

  isSaving.value = true
  try {
    await ipc.entry.update(selectedEntry.value.entryId, title, content)
    isEditing.value = false
    await selectEntry(selectedEntry.value.entryId)
    // Reload cluster list to reflect title updates
    await loadClusters()
  } catch (err: any) {
    alert(`儲存失敗: ${err.message}`)
  } finally {
    isSaving.value = false
  }
}

async function handleDeleteEntry() {
  if (!selectedEntry.value) return
  if (!confirm(`確定要永久刪除知識條目「${selectedEntry.value.title}」嗎？此動作將會刪除其所有歷史版本且無法復原。`)) return

  try {
    await ipc.entry.delete(selectedEntry.value.entryId)
    selectedEntry.value = null
    selectedEntryId.value = null
    await loadClusters()
  } catch (err: any) {
    alert(`刪除失敗: ${err.message}`)
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
    <!-- 第一欄：主題分群清單 -->
    <div class="w-1/4 border-r border-white/5 flex flex-col h-full bg-[#0a0c10] shrink-0">
      <div class="p-4 border-b border-white/5 flex items-center justify-between">
        <span class="text-xs font-semibold text-[#828b9a] uppercase tracking-wider">主題分群</span>
        <button
          @click="triggerRecluster"
          :disabled="isReclustering"
          class="bg-sky-500/10 hover:bg-sky-500/20 disabled:opacity-50 text-sky-400 border border-sky-500/20 rounded-md px-2 py-1 text-[10px] font-medium transition flex items-center gap-1.5"
        >
          <svg v-if="isReclustering" class="w-2.5 h-2.5 animate-spin text-sky-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
          </svg>
          重新計算分群
        </button>
      </div>

      <div class="flex-1 overflow-y-auto p-3 space-y-2">
        <div v-if="isLoading && clusters.length === 0" class="text-center py-12 text-xs text-[#828b9a]">
          載入分群中...
        </div>
        <div v-else-if="clusters.length === 0" class="text-center py-12 text-xs text-[#828b9a]">
          目前無任何分群
        </div>
        
        <button
          v-for="cluster in clusters"
          :key="cluster.clusterId"
          @click="selectCluster(cluster)"
          :class="['w-full text-left p-3 rounded-lg border transition-all duration-150 relative overflow-hidden flex flex-col gap-1',
            selectedClusterId === cluster.clusterId
              ? 'bg-white/[0.02] border-sky-500/50 shadow-md shadow-sky-500/5'
              : 'bg-transparent border-transparent hover:bg-white/[0.015] hover:border-white/5']"
        >
          <div v-if="cluster.clusterId === '00000000-0000-0000-0000-000000000000'" class="absolute right-0 top-0 text-[8px] bg-white/5 text-[#828b9a] px-1.5 py-0.5 rounded-bl">
            噪音點
          </div>
          <span class="font-medium text-xs text-white line-clamp-1 group-hover:text-sky-400 transition-colors">{{ cluster.name }}</span>
          <span class="text-[10px] text-[#828b9a]">{{ cluster.entries.length }} 筆知識條目</span>
        </button>
      </div>
    </div>

    <!-- 第二欄：分群下的知識條目清單 -->
    <div class="w-1/4 border-r border-white/5 flex flex-col h-full bg-[#08090d] shrink-0">
      <div class="p-4 border-b border-white/5">
        <span class="text-xs font-semibold text-[#828b9a] uppercase tracking-wider">
          {{ selectedCluster ? `【${selectedCluster.name}】的條目` : '條目清單' }}
        </span>
      </div>

      <div class="flex-1 overflow-y-auto p-3 space-y-2">
        <div v-if="!selectedCluster" class="text-center py-12 text-xs text-[#828b9a]">
          請先從左側選擇一個主題分群
        </div>
        <div v-else-if="selectedCluster.entries.length === 0" class="text-center py-12 text-xs text-[#828b9a]">
          此分群下無任何知識條目
        </div>
        
        <button
          v-for="entry in selectedCluster?.entries || []"
          :key="entry.entryId"
          @click="selectEntry(entry.entryId)"
          :class="['w-full text-left p-3.5 rounded border transition-all duration-150 flex flex-col gap-2 relative overflow-hidden',
            selectedEntryId === entry.entryId
              ? 'bg-white/[0.02] border-sky-500'
              : 'bg-transparent border-transparent hover:bg-white/[0.015] hover:border-white/5']"
        >
          <div class="flex justify-between items-start gap-2">
            <span class="font-medium text-xs text-white group-hover:text-sky-400 transition-colors line-clamp-2">{{ entry.title }}</span>
            <span class="text-[9px] text-[#828b9a] font-mono shrink-0">v{{ entry.version }}</span>
          </div>
          <div class="text-[9px] text-[#828b9a] font-mono">{{ formatTime(entry.updatedAt) }}</div>
        </button>
      </div>
    </div>

    <!-- 第三欄：知識條目詳細內容（整合編輯模式） -->
    <div class="flex-1 flex flex-col h-full bg-[#090b0f] overflow-hidden">
      <div v-if="!selectedEntry" class="flex-1 flex flex-col items-center justify-center text-center p-8">
        <div class="w-12 h-12 rounded bg-white/[0.015] border border-white/5 flex items-center justify-center text-[#828b9a] mb-4">
          <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
          </svg>
        </div>
        <h3 class="text-xs font-semibold text-white mb-1">選取一筆知識條目</h3>
        <p class="text-[11px] text-[#828b9a] max-w-xs">從中間清單選取條目以查看詳細內文與進行編輯。</p>
      </div>

      <div v-else class="flex-1 flex flex-col min-h-0">
        <div class="px-6 py-4 border-b border-white/5 flex items-center justify-between gap-4">
          <div class="flex-1">
            <template v-if="isEditing">
              <input
                v-model="editTitle"
                type="text"
                placeholder="輸入條目標題..."
                class="w-full bg-[#121620] border border-white/10 rounded py-1.5 px-3 text-xs text-white placeholder-[#828b9a] focus:outline-none focus:border-sky-500 transition duration-150"
              />
              <p class="text-[10px] text-sky-400 mt-1.5 flex items-center gap-1 font-mono">
                <span class="w-1.5 h-1.5 rounded-full bg-sky-400 animate-pulse"></span>
                編輯模式 · 儲存後將自動建立新版本 (v{{ selectedEntry.version + 1 }})
              </p>
            </template>
            <template v-else>
              <h2 class="text-sm font-semibold text-white">{{ selectedEntry.title }}</h2>
              <p class="text-[10px] text-[#828b9a] mt-1">
                v{{ selectedEntry.version }} · {{ formatTime(selectedEntry.updatedAt) }}
              </p>
            </template>
          </div>
          <div class="flex items-center gap-2">
            <template v-if="isEditing">
              <button
                @click="handleSaveEdit"
                :disabled="isSaving"
                class="bg-sky-500 hover:bg-sky-600 disabled:bg-sky-500/50 text-white rounded px-3 py-1.5 text-[11px] font-medium transition flex items-center gap-1.5"
              >
                <svg v-if="isSaving" class="w-3 h-3 animate-spin text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
                </svg>
                儲存
              </button>
              <button
                @click="cancelEditing"
                :disabled="isSaving"
                class="bg-white/5 border border-white/10 hover:bg-white/10 text-white rounded px-3 py-1.5 text-[11px] font-medium transition"
              >
                取消
              </button>
            </template>
            <template v-else>
              <button
                @click="startEditing"
                class="bg-white/[0.015] border border-white/5 hover:bg-white/[0.03] text-white rounded px-3 py-1.5 text-[11px] font-medium transition"
              >
                編輯條目
              </button>
              <button
                @click="handleDeleteEntry"
                class="bg-red-500/10 border border-red-500/20 hover:bg-red-500/20 text-red-400 rounded px-3 py-1.5 text-[11px] font-medium transition"
              >
                刪除條目
              </button>
            </template>
          </div>
        </div>

        <div class="flex-1 flex min-h-0">
          <div class="flex-1 overflow-y-auto p-6 flex flex-col min-h-0">
            <textarea
              v-if="isEditing"
              v-model="editContent"
              placeholder="請輸入 Markdown 格式的知識內容..."
              class="w-full flex-1 min-h-[300px] resize-none bg-[#121620] border border-white/5 rounded-lg p-4 text-xs text-white placeholder-[#828b9a] focus:outline-none focus:border-sky-500 focus:ring-1 focus:ring-sky-500/30 transition duration-150 font-mono leading-relaxed"
            ></textarea>
            <div v-else class="prose max-w-none text-slate-300">
              <MarkdownViewer :content="selectedEntry.content" />
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
