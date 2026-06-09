<script setup lang="ts">
import { ref } from 'vue'
import { ipc } from '../ipc/bridge'

const ingestSource = ref('')
const ingestContent = ref('')
const isIngesting = ref(false)
const ingestStep = ref(0)
const showIngestSuccess = ref(false)
const dragOver = ref(false)

const ingestSteps = [
  '儲存原始文件',
  '產生知識大綱',
  '計算 Embedding',
  '向量搜尋與路由',
  '建立或合併知識條目',
  '完成索引更新',
]

async function startIngest() {
  if (!ingestContent.value.trim()) {
    alert('請輸入或拖放文件內容！')
    return
  }

  isIngesting.value = true
  showIngestSuccess.value = false
  ingestStep.value = 0

  const stepTimer = window.setInterval(() => {
    if (ingestStep.value < ingestSteps.length - 2) {
      ingestStep.value++
    }
  }, 1000)

  try {
    const source = ingestSource.value.trim() || '手動導入'
    await ipc.ingest(ingestContent.value, source)

    window.clearInterval(stepTimer)
    ingestStep.value = ingestSteps.length - 1

    window.setTimeout(() => {
      isIngesting.value = false
      showIngestSuccess.value = true
      ingestSource.value = ''
      ingestContent.value = ''
    }, 800)
  } catch (err: any) {
    window.clearInterval(stepTimer)
    isIngesting.value = false
    alert(`文件導入失敗: ${err.message}`)
  }
}

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
</script>

<template>
  <div class="h-full overflow-y-auto p-8 flex justify-center">
    <div class="w-full max-w-2xl space-y-4">
      <div class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-4">
        <h2 class="text-xs font-semibold text-white tracking-widest uppercase">導入文件</h2>

        <div class="space-y-1">
          <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">Source</label>
          <input
            v-model="ingestSource"
            type="text"
            placeholder="例如: meeting-notes.md"
            class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white placeholder-[#828b9a] focus:outline-none focus:border-sky-500 transition"
            :disabled="isIngesting"
          />
        </div>

        <div class="space-y-1">
          <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">Content</label>
          <div
            @dragover.prevent="dragOver = true"
            @dragleave.prevent="dragOver = false"
            @drop.prevent="handleDrop"
            :class="['relative border border-dashed rounded transition-all duration-150 flex flex-col',
              dragOver ? 'border-sky-500 bg-sky-500/5' : 'border-white/5 hover:border-white/10 bg-[#121620]/30']"
          >
            <textarea
              v-model="ingestContent"
              rows="10"
              placeholder="貼上文件內容，或拖放 .txt / .md / .json 檔案..."
              class="w-full bg-transparent border-0 p-3 text-xs text-slate-200 placeholder-[#828b9a] focus:outline-none resize-y"
              :disabled="isIngesting"
            ></textarea>
            <div class="absolute right-3 bottom-3 text-[9px] text-[#828b9a] pointer-events-none uppercase font-mono tracking-wider">
              Drag & Drop Ready
            </div>
          </div>
        </div>

        <button
          @click="startIngest"
          :disabled="isIngesting || !ingestContent.trim()"
          class="w-full bg-sky-600 hover:bg-sky-700 text-white rounded py-2.5 text-xs font-semibold transition disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
        >
          <span v-if="!isIngesting">開始導入</span>
          <span v-else>處理中...</span>
        </button>
      </div>

      <div v-if="isIngesting" class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-3">
        <h3 class="text-[9px] font-bold text-sky-400 tracking-widest uppercase">Ingestion Pipeline Running</h3>
        <div class="space-y-2.5">
          <div
            v-for="(step, index) in ingestSteps"
            :key="step"
            :class="['flex items-center gap-2.5 text-[11px] transition-opacity duration-150',
              index === ingestStep ? 'text-white font-medium' : index < ingestStep ? 'text-emerald-400 opacity-60' : 'text-[#828b9a] opacity-30']"
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

      <div v-if="showIngestSuccess" class="bg-emerald-500/5 border border-emerald-500/15 rounded p-5 flex gap-3.5 items-start">
        <div class="shrink-0 w-7 h-7 rounded bg-emerald-500/10 flex items-center justify-center text-emerald-400">
          <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5">
            <path stroke-linecap="round" stroke-linejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        </div>
        <div>
          <h3 class="text-xs font-semibold text-white mb-0.5">文件導入完成</h3>
          <p class="text-[11px] text-[#828b9a] leading-relaxed">知識條目與向量索引已更新。</p>
        </div>
      </div>
    </div>
  </div>
</template>
