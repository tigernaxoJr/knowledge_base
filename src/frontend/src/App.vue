<script setup lang="ts">
import { ref } from 'vue'
import SearchView from './views/SearchView.vue'
import IngestView from './views/IngestView.vue'
import ConfigView from './views/ConfigView.vue'
import ClusterView from './views/ClusterView.vue'

type TabKey = 'search' | 'clusters' | 'ingest' | 'config'

const currentTab = ref<TabKey>('search')

const tabs: Array<{ key: TabKey; label: string; title: string; icon: string }> = [
  { key: 'search', label: '知識搜尋', title: '語意搜尋 / RAG Query', icon: 'M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z' },
  { key: 'clusters', label: '分群檢視', title: '主題分群 / Topic Clustering', icon: 'M4 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2v-2zM14 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z' },
  { key: 'ingest', label: '文件導入', title: '文件導入 / Knowledge Ingestion', icon: 'M9 13h6m-3-3v6m5 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z' },
  { key: 'config', label: '模型設定', title: '模型與端點設定 / Configuration', icon: 'M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z' },
]
</script>

<template>
  <div class="flex h-screen bg-[#090b0f] text-[#f3f4f6] font-sans selection:bg-sky-500/20 overflow-hidden">
    <aside class="w-60 bg-[#0d0f14] border-r border-white/5 flex flex-col justify-between shrink-0">
      <div>
        <div class="p-5 border-b border-white/5 flex items-center gap-2.5">
          <div class="w-7 h-7 rounded bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-sky-400">
            <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M9.663 17h4.673M12 3v1m6.364.364l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
            </svg>
          </div>
          <span class="font-bold text-xs tracking-widest text-white uppercase">KnowledgeOS</span>
        </div>

        <nav class="p-3 space-y-1">
          <button
            v-for="tab in tabs"
            :key="tab.key"
            @click="currentTab = tab.key"
            :class="['w-full flex items-center gap-3 px-3 py-2.5 rounded text-xs font-medium transition duration-150 border-l-2',
              currentTab === tab.key
                ? 'bg-white/[0.03] border-sky-500 text-sky-400'
                : 'border-transparent text-[#828b9a] hover:bg-white/[0.015] hover:text-white']"
          >
            <svg class="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" :d="tab.icon" />
              <path v-if="tab.key === 'config'" stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
            {{ tab.label }}
          </button>
        </nav>
      </div>

      <div class="p-4 border-t border-white/5 text-[10px] text-[#828b9a]">
        <div class="flex items-center gap-2">
          <span class="w-1.5 h-1.5 rounded-full bg-sky-500"></span>
          <span>本機知識庫</span>
        </div>
      </div>
    </aside>

    <main class="flex-1 flex flex-col min-w-0 bg-[#090b0f]">
      <header class="h-14 border-b border-white/5 bg-[#0d0f14]/50 backdrop-blur-md flex items-center justify-between px-6 shrink-0 z-10">
        <h1 class="text-xs font-semibold text-white tracking-widest uppercase">
          {{ tabs.find(tab => tab.key === currentTab)?.title }}
        </h1>
      </header>

      <div class="flex-1 overflow-hidden">
        <SearchView v-if="currentTab === 'search'" />
        <ClusterView v-else-if="currentTab === 'clusters'" />
        <IngestView v-else-if="currentTab === 'ingest'" />
        <ConfigView v-else />
      </div>
    </main>
  </div>
</template>
