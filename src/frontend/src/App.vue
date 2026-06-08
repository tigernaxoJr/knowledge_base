<script setup lang="ts">
</script>

<template>
  <router-view v-if="false" />
  <div class="app-shell">
    <!-- TODO: 加入 RouterView 與側邊導航後替換此 placeholder -->
    <p style="color: var(--color-text-muted); padding: 2rem;">
      個人知識庫 — 載入中...
    </p>
  </div>
</template>

<style scoped>
.app-shell {
  display: flex;
  height: 100%;
}
</style>
