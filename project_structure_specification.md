# 專案目錄結構規格 (Project Structure Specification)

本文件定義個人知識庫系統的專案目錄結構、各子專案職責邊界，以及解決方案組成。

---

## 1. 解決方案總覽

系統由三個子專案組成，採用 **.NET Solution** 統一管理後端，前端為獨立 npm 專案：

| 子專案 | 類型 | 技術棧 | 職責 |
|--------|------|--------|------|
| `Assistant.Core` | .NET Class Library | .NET 10 Native AOT | 純業務邏輯，無 UI 依賴 |
| `Assistant.App` | .NET Executable | .NET 10 Native AOT | App 進入點、WebView 宿主、IPC 橋接 |
| `frontend` | npm 專案 | Vue 3 + TypeScript + Tailwind CSS | 使用者介面（編譯為靜態資產嵌入 App） |

---

## 2. 完整目錄結構

```
assistant/
├── src/
│   │
│   ├── Assistant.Core/                  ← .NET Class Library（業務邏輯層）
│   │   ├── Assistant.Core.csproj
│   │   ├── Ingestion/                   # 文件導入 Pipeline
│   │   │   ├── IngestionService.cs      # 接收新文件、呼叫大綱生成
│   │   │   └── OutlineGenerator.cs      # LLM 大綱生成（400 字摘要）
│   │   ├── Search/                      # 向量檢索與路由決策
│   │   │   ├── VectorSearchEngine.cs    # LanceDB Cosine Similarity 查詢
│   │   │   └── RoutingDecision.cs       # 相似度閾值路由（新建 / Merge）
│   │   ├── Clustering/                  # HDBSCAN 聚群引擎
│   │   │   └── HdbscanEngine.cs         # 冷啟動 & 定期維護呼叫
│   │   ├── LlmClient/                   # LLM & Embedding 客戶端
│   │   │   ├── LlmClientFactory.cs      # 依設定動態建立 HTTP Client
│   │   │   ├── ChatClient.cs            # LLM Chat 呼叫（大綱生成、Merge）
│   │   │   └── EmbeddingClient.cs       # Embedding 向量計算
│   │   ├── KnowledgeBase/               # 知識條目 CRUD & 版本控制
│   │   │   ├── KnowledgeEntryService.cs # 新建、Merge、Rollback
│   │   │   └── VersionControlService.cs # 歷史版本備份與還原
│   │   ├── Storage/                     # 資料庫存取層
│   │   │   ├── LanceDbClient.cs         # LanceDB Native Interop 封裝
│   │   │   └── SqliteRepository.cs      # SQLite / LiteDB 設定 & 元資料
│   │   └── Config/                      # 設定模型與讀寫
│   │       ├── AppSettings.cs           # LlmConfig / EmbeddingConfig DTO
│   │       └── ConfigService.cs         # 設定動態載入、加密儲存
│   │
│   ├── Assistant.App/                   ← .NET Executable（App 主程式）
│   │   ├── Assistant.App.csproj         # PublishAot=true，MSBuild 整合前端 Build
│   │   ├── Program.cs                   # 進入點，初始化 DI 容器
│   │   ├── WebViewHost.cs               # WebView2 / Photino 啟動 & Local Scheme 設定
│   │   ├── IpcBridge.cs                 # postMessage 收發、命令路由至 Core
│   │   └── ResourceLoader.cs            # 從 Embedded Resources 回應靜態資產
│   │
│   └── frontend/                        ← Vue 3 獨立 npm 專案（UI 層）
│       ├── package.json
│       ├── vite.config.ts               # base: './'，固定輸出檔名（無 hash）
│       ├── tsconfig.json
│       ├── tailwind.config.js
│       ├── index.html
│       ├── src/
│       │   ├── main.ts                  # Vue App 進入點
│       │   ├── App.vue
│       │   ├── components/              # 可複用 UI 元件
│       │   ├── views/                   # 頁面元件
│       │   ├── composables/             # Composition API 邏輯複用
│       │   ├── stores/                  # Pinia 狀態管理
│       │   └── ipc/
│       │       └── bridge.ts            # window.chrome.webview.postMessage 封裝
│       └── dist/                        ← Vite build 輸出（不納入版控）
│
├── tests/
│   └── Assistant.Core.Tests/            ← xUnit 單元測試
│       ├── Assistant.Core.Tests.csproj
│       ├── Ingestion/
│       ├── Search/
│       └── KnowledgeBase/
│
├── docs/
│   ├── system_architecture_specification.md   # 系統架構規格
│   └── project_structure_specification.md     # 本文件
│
├── .gitignore
├── Assistant.sln                        ← .NET Solution（包含 Core + App + Tests）
└── README.md
```

---

## 3. 各子專案職責邊界

### 3.1 `Assistant.Core` — 業務邏輯層

*   **原則**：完全無 UI 依賴，不引用 WebView2 / Photino 任何套件。
*   **可測試性**：所有服務以介面（`interface`）定義，支援 xUnit 單元測試與 Mock 注入。
*   **AOT 合規**：所有 JSON 序列化使用 Source Generator，DTO 標記 `[JsonSerializable]`。
*   **對外暴露**：透過 DI 介面供 `Assistant.App` 呼叫，不直接暴露實作細節。

### 3.2 `Assistant.App` — App 進入點與 WebView 宿主

*   **職責**：
    *   啟動 WebView2（Windows）或 Photino（跨平台），載入 `app://frontend/index.html`。
    *   實作 Local URI Scheme 攔截，將 `app://*` 請求映射至嵌入資源。
    *   IPC 橋接：接收前端 `postMessage` → 解析命令 → 呼叫 `Core` 對應服務 → 回傳 JSON 結果。
    *   MSBuild Target：在 `dotnet build` / `dotnet publish` 前自動執行 `npm run build`，並將 `dist/` 內容宣告為 Embedded Resources。
*   **不含業務邏輯**：所有 AI、DB、LLM 操作均委派給 `Assistant.Core`。

### 3.3 `frontend/` — 使用者介面

*   **獨立開發**：可在不啟動後端的情況下，以 `npm run dev` 開發 UI（搭配 Mock IPC）。
*   **IPC 封裝**：`src/ipc/bridge.ts` 統一封裝 `window.chrome.webview.postMessage`，避免前端各元件直接耦合 IPC 細節。
*   **生產建置**：`npm run build` 輸出 `dist/`，由 `Assistant.App.csproj` 的 MSBuild Target 打包為嵌入資源。

---

## 4. 依賴關係

```
frontend/  ──(build → dist/)──▶  Assistant.App
                                       │
                                       │ 引用
                                       ▼
                                 Assistant.Core
                                       │
                                       │ 測試
                                       ▼
                              Assistant.Core.Tests
```

*   `frontend` 與 `.NET` 專案之間**無直接程式碼依賴**，僅透過 Build Pipeline（MSBuild 嵌入）與 IPC 協議（JSON over postMessage）銜接。
*   `Assistant.App` 引用 `Assistant.Core`，但 `Core` **不引用** `App`（單向依賴）。
*   `Assistant.Core.Tests` 引用 `Assistant.Core`，不引用 `App` 或 `frontend`。

---

## 5. `.gitignore` 建議排除項目

```gitignore
# .NET
bin/
obj/
*.user

# Frontend
src/frontend/node_modules/
src/frontend/dist/

# 系統
.DS_Store
Thumbs.db
```

---

## 6. Solution 設定（`Assistant.sln`）

```
dotnet new sln -n Assistant
dotnet sln add src/Assistant.Core/Assistant.Core.csproj
dotnet sln add src/Assistant.App/Assistant.App.csproj
dotnet sln add tests/Assistant.Core.Tests/Assistant.Core.Tests.csproj
```

前端 `frontend/` 為獨立 npm 專案，不加入 `.sln`，由 `Assistant.App.csproj` 的 MSBuild Target 驅動建置。
