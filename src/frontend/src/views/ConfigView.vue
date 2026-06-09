<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ipc } from '../ipc/bridge'

const config = ref({
  llmConfig: { endpoint: '', apiKey: '', modelName: '' },
  embeddingConfig: { endpoint: '', apiKey: '', modelName: '' },
})
const showLlmKey = ref(false)
const showEmbedKey = ref(false)
const isSavingConfig = ref(false)
const testStatus = ref<'idle' | 'testing' | 'success' | 'failed'>('idle')
const testErrorMessage = ref<string | null>(null)

onMounted(() => {
  void loadConfig()
})

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
      config.value.llmConfig.modelName,
    )
    testStatus.value = res.success ? 'success' : 'failed'
    testErrorMessage.value = res.errorMessage || null
  } catch (err: any) {
    testStatus.value = 'failed'
    testErrorMessage.value = err.message
  }
}
</script>

<template>
  <div class="h-full overflow-y-auto p-8 flex justify-center">
    <div class="w-full max-w-2xl space-y-4">
      <div class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-4">
        <h2 class="text-xs font-semibold text-white tracking-widest uppercase">LLM Config</h2>
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div class="space-y-1">
            <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Endpoint</label>
            <input v-model="config.llmConfig.endpoint" type="text" placeholder="https://api.openai.com/v1" class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition" />
          </div>
          <div class="space-y-1">
            <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">Model</label>
            <input v-model="config.llmConfig.modelName" type="text" placeholder="gpt-4o" class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition" />
          </div>
          <div class="space-y-1 md:col-span-2">
            <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Key</label>
            <div class="relative">
              <input v-model="config.llmConfig.apiKey" :type="showLlmKey ? 'text' : 'password'" placeholder="sk-..." class="w-full bg-[#121620] border border-white/5 rounded py-2 pl-3 pr-9 text-xs text-white focus:outline-none focus:border-sky-500 transition" />
              <button @click="showLlmKey = !showLlmKey" class="absolute right-2.5 top-2 text-[#828b9a] hover:text-white">{{ showLlmKey ? 'Hide' : 'Show' }}</button>
            </div>
          </div>
        </div>
      </div>

      <div class="bg-[#11141a]/40 border border-white/5 rounded p-5 space-y-4">
        <h2 class="text-xs font-semibold text-white tracking-widest uppercase">Embedding Config</h2>
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div class="space-y-1">
            <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Endpoint</label>
            <input v-model="config.embeddingConfig.endpoint" type="text" placeholder="https://api.openai.com/v1" class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition" />
          </div>
          <div class="space-y-1">
            <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">Model</label>
            <input v-model="config.embeddingConfig.modelName" type="text" placeholder="text-embedding-3-small" class="w-full bg-[#121620] border border-white/5 rounded py-2 px-3 text-xs text-white focus:outline-none focus:border-sky-500 transition" />
          </div>
          <div class="space-y-1 md:col-span-2">
            <label class="text-[10px] text-[#828b9a] font-medium tracking-wide uppercase">API Key</label>
            <div class="relative">
              <input v-model="config.embeddingConfig.apiKey" :type="showEmbedKey ? 'text' : 'password'" placeholder="sk-..." class="w-full bg-[#121620] border border-white/5 rounded py-2 pl-3 pr-9 text-xs text-white focus:outline-none focus:border-sky-500 transition" />
              <button @click="showEmbedKey = !showEmbedKey" class="absolute right-2.5 top-2 text-[#828b9a] hover:text-white">{{ showEmbedKey ? 'Hide' : 'Show' }}</button>
            </div>
          </div>
        </div>
      </div>

      <div v-if="testStatus !== 'idle'" :class="['p-4 rounded border flex gap-3.5 items-start transition',
        testStatus === 'testing' ? 'bg-white/[0.015] border-white/5 text-[#828b9a]' :
        testStatus === 'success' ? 'bg-emerald-500/5 border-emerald-500/15 text-emerald-400' :
        'bg-rose-500/5 border-rose-500/15 text-rose-400']"
      >
        <div>
          <h4 class="text-xs font-bold text-white mb-0.5">
            <template v-if="testStatus === 'testing'">測試連線中</template>
            <template v-else-if="testStatus === 'success'">LLM 連線成功</template>
            <template v-else>LLM 連線失敗</template>
          </h4>
          <p class="text-[11px] leading-relaxed text-[#828b9a]">{{ testStatus === 'failed' ? testErrorMessage : '端點已回應。' }}</p>
        </div>
      </div>

      <div class="flex gap-4">
        <button @click="testConnection" :disabled="testStatus === 'testing'" class="flex-1 bg-white/[0.015] border border-white/5 hover:bg-white/[0.03] text-white rounded py-2.5 text-xs font-semibold transition">
          測試 LLM 連線
        </button>
        <button @click="saveConfig" :disabled="isSavingConfig" class="flex-1 bg-sky-600 hover:bg-sky-700 text-white rounded py-2.5 text-xs font-semibold transition">
          {{ isSavingConfig ? '儲存中...' : '儲存設定' }}
        </button>
      </div>
    </div>
  </div>
</template>
