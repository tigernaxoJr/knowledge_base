<script setup lang="ts">
import { computed } from 'vue'
import { renderMarkdown } from '../composables/useMarkdownRenderer'

const props = defineProps<{
  content: string
}>()

const html = computed(() => renderMarkdown(props.content))
</script>

<template>
  <div class="markdown-body" v-html="html"></div>
</template>

<style>
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
