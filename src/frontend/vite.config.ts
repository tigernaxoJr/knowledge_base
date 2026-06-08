import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
  ],

  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },

  build: {
    // 輸出目錄
    outDir: 'dist',
    emptyOutDir: true,
    // 關閉 source map（減少嵌入資源體積）
    sourcemap: false,
    rollupOptions: {
      output: {
        // 固定輸出檔名（不含 hash），簡化 .NET Embedded Resource 路徑引用
        entryFileNames: 'assets/index.js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]',
      },
    },
  },

  // 相對路徑：確保在 WebView2 自訂 Local URI Scheme (app://) 下正確載入
  base: './',
})
