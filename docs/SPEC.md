# AI Digital Doll 系統規格書

| 項目 | 內容 |
|---|---|
| 文件版本 | v0.1（草案） |
| 日期 | 2026-10-24 |
| 目標平台 | Windows 10 (22H2) / Windows 11，x64 |
| 文件狀態 | 規劃階段，待確認後進入 POC |

---

## 1. 專案概述

### 1.1 產品願景

一款常駐在 Windows 桌面的 **3D AI 數位人偶**。使用者先用內建捏人系統打造專屬角色，角色會在桌面上自由活動；使用者對角色說話或打字，角色把指令轉交給使用者綁定的 AI（雲端或本地模型），再以語音或對話泡泡回答，並依回答內容做出對應的動作與表情。

### 1.2 三大功能模組

| 模組 | 說明 |
|---|---|
| **A. 捏人系統（Character Creator）** | 性別 → 體型 → 臉部與五官 → 身材細節 → 髮型 → 服裝 → 存檔 |
| **B. 桌面人偶（Desktop Mascot）** | 透明視窗中的 3D 角色，會走動、坐在視窗上、被拖曳、睡覺等，具備真實骨架動畫 |
| **C. AI 代理人（AI Agent Bridge）** | 綁定 ChatGPT / Claude / DeepSeek / Gemini / Grok / 本地模型，轉交指令並以語音、泡泡、動作回覆 |

### 1.3 不在第一版範圍內（Non-Goals）

- 多人連線、角色分享社群平台
- 手機版、macOS 版
- 角色自行操作電腦（Computer Use／自動化操作），列為未來擴充
- 成人內容（見 §10 內容政策）

---

## 2. 技術選型

### 2.1 總覽

| 層級 | 選擇 | 理由 |
|---|---|---|
| 遊戲引擎 / 主程式 | **Unity 6（URP）+ C#** | 3D 角色、動畫、物理、Shader 生態最成熟；VRM、口型同步、動畫重定向都有現成套件 |
| 角色格式（執行期） | **VRM 1.0**（UniVRM） | 開放標準，內建人形骨架對應、表情（Expression）、視線、彈簧骨（頭髮、裙擺） |
| 捏人素材來源 | **MakeHuman CC0 素材**（見 §3） | 大量成熟的身體/臉部變形 target、骨架、可貼合身形的衣服格式，且授權為 CC0 |
| 桌面透明視窗 | Win32 API（P/Invoke） | `WS_EX_LAYERED`、`DwmExtendFrameIntoClientArea`、逐像素點擊穿透 |
| AI 介接 | 自寫 C# Provider 抽象層 + `HttpClient` 串流（SSE） | 各家 API 差異不大，自寫較好掌握，不被 SDK 綁死 |
| 語音輸出 TTS | Windows 內建語音（離線）/ OpenAI TTS / Azure / ElevenLabs / 本地 Piper | 可插拔 |
| 語音輸入 STT | whisper.cpp（本地）/ 雲端 Whisper / Windows 語音辨識 | 可插拔 |
| 口型同步 | uLipSync（音量 + 母音辨識）或 TTS 回傳的 viseme | |
| 資料儲存 | `%AppData%\AIDigitalDoll\`，JSON + 二進位素材 | |
| 金鑰保存 | Windows DPAPI（`ProtectedData`）或 Windows 認證管理員 | API Key 不得明文存檔 |
| 安裝包 | MSIX 或 Inno Setup | |

> **替代方案**：Godot 4（MIT 授權、內建透明視窗支援）。若團隊偏好完全開源引擎可考慮，但 VRM、口型同步、動畫資源的生態不如 Unity，開發時間預估多 30% 以上。**建議採用 Unity。**

### 2.2 整體架構

```
┌──────────────────────── AI Digital Doll（單一 Unity 程式）────────────────────────┐
│                                                                                   │
│  ┌──────────────┐   ┌──────────────────────┐   ┌──────────────────────────────┐   │
│  │ 捏人系統場景  │   │ 桌面人偶場景          │   │ 設定 / 聊天紀錄 UI           │   │
│  │ Creator      │   │ Mascot               │   │ (UI Toolkit)                 │   │
│  └──────┬───────┘   └─────────┬────────────┘   └──────────────┬───────────────┘   │
│         │ 產出 VRM + .doll    │ 載入 VRM                       │                   │
│  ┌──────▼───────────────────────────────────────────────────────────────────┐     │
│  │ Core 服務層                                                              │     │
│  │  CharacterService │ AnimationDirector │ BehaviorAI(行為樹) │ DesktopEnv  │     │
│  │  AgentBridge(AI 路由) │ ActionMapper(回答→動作) │ VoiceService(TTS/STT)   │     │
│  │  StorageService │ SecretStore(DPAPI) │ PluginHost(未來擴充)               │     │
│  └──────┬─────────────────────────────┬──────────────────────────────────────┘     │
│         │ Win32 P/Invoke               │ HTTPS / localhost                         │
└─────────┼──────────────────────────────┼───────────────────────────────────────────┘
          ▼                              ▼
  Windows 桌面（視窗列舉、        OpenAI / Anthropic / DeepSeek / Google /
  工作列位置、多螢幕、DPI）         xAI / Ollama / LM Studio
```

---

## 3. 捏人系統與 MakeHuman 整合評估

### 3.1 MakeHuman 授權分析（已查證原始碼庫）

來源：<https://github.com/makehumancommunity/makehuman>（`LICENSE.md`）

| 部分 | 授權 | 對我們的影響 |
|---|---|---|
| 程式碼（Python、Shader、UI 圖示） | **AGPL v3** | **不能**把它的程式碼複製、改寫進我們的程式，否則整個 App 都必須以 AGPL 開源 |
| 素材：基礎網格、proxy、**targets 與 modifiers**、貼圖、衣服（MHCLO）、姿勢與表情 | **CC0 1.0** | 可自由使用、修改、商用、重新散布，無需標示 |
| MakeHuman 匯出的檔案（FBX、OBJ、存檔） | 官方聲明不主張任何權利 | 使用者用 MakeHuman 捏好的角色可直接匯入我們的 App |
| 社群素材庫（第三方上傳） | 各自不同（CC0、CC-BY…） | 須逐一檢查授權，建議只收錄 CC0 / CC-BY 並保留標示 |

**結論：MakeHuman 非常適合，但用法是「使用它的 CC0 素材資料，自己用 C# 實作變形邏輯」，而不是把 MakeHuman 程式本體包進來。**

### 3.2 MakeHuman 能提供什麼

| 我們的需求 | MakeHuman 對應能力 |
|---|---|
| 性別 | Macro modifier：Gender（男 ↔ 女連續滑桿），另有 Age、Muscle、Weight、Height、Proportions、族裔混合 |
| 臉、五官 | 頭型、額頭、眉毛、眼睛（大小、位置、角度、眼角）、鼻子（鼻樑、鼻翼、鼻尖）、嘴（唇厚、嘴角、寬度）、耳朵、下巴、臉頰等數百個 target |
| 身材 | 軀幹、胸部（大小、緊實度、位置、間距）、腰、腹部、臀部、骨盆、上臂、前臂、手、大腿、小腿、腳、脖子；另有以公分為單位的「測量（Measurement）」modifier |
| 骨架 | 預設骨架（含臉部骨骼）與多種精簡骨架（含適合遊戲引擎的版本）；關節位置會依體型自動重新計算 |
| 服裝 | MHCLO 格式：衣服頂點綁定在身體網格上，**身形改變時衣服自動跟著貼合** |
| 髮型、眉毛、睫毛、眼睛、牙齒 | 皆有內建素材 |
| 表情 | 以臉部骨骼 pose 定義的表情庫（可烘焙成 blendshape） |

### 3.3 三種整合方案比較

| 方案 | 做法 | 優點 | 缺點 | 建議 |
|---|---|---|---|---|
| **A. 外部編輯器** | 使用者自行安裝 MakeHuman 捏人 → 匯出 FBX → 我們的 App 匯入並轉成 VRM | 最快上線，幾乎零開發 | 兩套軟體、介面是英文、操作門檻高、無法即時預覽桌面效果 | 當作 **POC 階段** 與「進階使用者匯入」管道 |
| **B. 素材移植 + C# 重寫變形引擎** | 寫離線轉檔工具把 MakeHuman 的 CC0 素材（base mesh、targets、modifiers JSON、骨架、MHCLO 衣服）轉成 Unity 格式；在 Unity 內用 C#（Burst/Job System 或 Compute Shader）重新實作 modifier → target 權重 → 頂點變形 → 骨架重算 → 衣服貼合 | 整合在同一個 App、中文介面、即時預覽、授權乾淨（只用 CC0 資料、不碰 AGPL 程式碼） | 開發量最大（約 2–3 個月） | **正式版主要方案** |
| C. 背景呼叫 MakeHuman | 把 MakeHuman 當子程序執行，透過它的 `8_server_socket` 外掛遠端操控 | 不必重寫變形邏輯 | 需夾帶 Python + PyQt5 執行環境（數百 MB）、AGPL 要提供原始碼、版本相依脆弱、即時性差 | 不建議 |

> **重要：方案 B 必須採「淨室（clean-room）實作」**——可以閱讀 MakeHuman 的公開文件與 CC0 資料格式（例如 `modeling_modifiers.json`、`.target`、`.mhclo`、`.mhskel` 的欄位意義），但**不得逐行翻譯它的 AGPL Python 程式碼**。建議由一人整理資料格式規格文件，另一人依規格寫 C#。

### 3.4 方案 B 技術細節

**離線轉檔管線（開發期執行，產出隨 App 散布）**

```
MakeHuman CC0 素材
  ├─ base.obj（基礎網格，含輔助幾何）
  ├─ targets/*.target（稀疏頂點位移：頂點索引 + Δxyz）
  ├─ modifiers/*.json（滑桿 → target 組合規則、左右對稱、相依關係）
  ├─ rigs/*.mhskel（關節位置以「頂點群平均」定義）
  ├─ clothes/*.mhclo（每個衣服頂點 = 3 個身體頂點的重心座標 + 偏移）
  └─ poses/expressions
        │  自寫轉檔工具（Python 或 C# CLI，非 MakeHuman 程式碼）
        ▼
Unity 資源
  ├─ BaseMesh.asset
  ├─ Targets.bin（所有 target 打包成稀疏二進位，啟動時載入 GPU Buffer）
  ├─ Modifiers.json（轉成我們自己的 schema，加上中文名稱與分類）
  ├─ Skeleton.json（關節 ↔ 頂點群對應）
  └─ Clothes/*.asset（網格 + 綁定資料 + 材質）
```

**執行期流程（捏人畫面）**

1. 使用者拖動滑桿 → 計算每個 target 的權重（macro modifier 會在多個 target 之間做多維內插，例如「女性 × 年輕 × 中等肌肉 × 中等體重」）。
2. Compute Shader：`最終頂點 = 基礎頂點 + Σ(權重ᵢ × Δᵢ)`，目標每次滑動 < 16 ms。
3. 依新頂點位置重算骨架關節（頂點群平均值）並更新 bind pose。
4. 衣服依 MHCLO 綁定資料重新貼合；重算法線與切線。
5. 按「存檔」時：烘焙成單一網格 → 產生表情 blendshape（以臉部骨架擺出表情後擷取頂點差）→ 匯出 **VRM 1.0**。

**桌面模式只載入烘焙好的 VRM**，不需要變形引擎，執行期效能負擔最小。

### 3.5 MakeHuman 的限制與因應

| 限制 | 因應 |
|---|---|
| 畫風偏寫實，不是動漫風 | ① 自製卡通/動漫風 Toon Shader（MToon）＋大眼、小鼻 preset；② 另外支援 **匯入 VRoid Studio 等工具做的 VRM**，動漫風使用者走這條路 |
| 內建衣服數量少、風格較舊 | 用 Blender + MPFB2 外掛自製 MHCLO 衣服（工具 GPL 不影響產出物授權）；規劃衣服素材包 |
| 原始碼庫最後提交為 2024-06，更新趨緩 | 我們只依賴其**素材資料**，轉檔後即與上游脫鉤，不受影響 |
| 部分 target 涉及成人部位 | 轉檔時直接排除，不收錄（見 §10） |
| 基礎網格多邊形數中等（約 1.3 萬頂點身體） | 桌面角色足夠；可用 subdivision 或高解析 proxy 改善 |

### 3.6 捏人流程（UI）

```
① 起始 ─ 選性別（男 / 女 / 自訂滑桿）＋ 預設範本（10+ 款）
② 體型 ─ 年齡、身高、體重、肌肉量、比例（頭身比）
③ 臉部 ─ 臉型 → 額頭 → 眉 → 眼 → 鼻 → 嘴 → 耳 → 下巴 → 臉頰
④ 身材 ─ 脖子、肩、胸、腰、腹、臀、骨盆、上臂、前臂、手、大腿、小腿、腳
           每部位有「簡易模式（3–5 個滑桿）」與「進階模式（全部滑桿）」
⑤ 外觀 ─ 膚色、眼睛顏色、髮型、髮色、眉型、睫毛、妝容（貼圖疊層）
⑥ 服裝 ─ 上衣、下身、連身、鞋、襪、配件；顏色可調；自動貼合身形
⑦ 個性 ─ 名字、聲音（TTS 音色）、說話風格（系統提示詞）、預設 AI
⑧ 存檔 ─ 寫入 .doll 存檔 + 匯出 VRM；可另存為新範本
```

功能需求：

- **CR-01** 所有滑桿即時預覽，支援還原/重做（Undo/Redo，至少 50 步）
- **CR-02** 左右對稱開關（預設開啟）
- **CR-03** 隨機產生（可鎖定特定部位不隨機）
- **CR-04** 鏡頭：旋轉、縮放、快速聚焦到臉/上半身/全身
- **CR-05** 預覽動作：待機、走路、揮手，檢查衣服穿模
- **CR-06** 存檔格式見 §8；可匯入 MakeHuman 匯出的 FBX、以及任意 VRM 0.x/1.0

---

## 4. 桌面人偶模組

### 4.1 透明視窗技術

- 建立全螢幕（或跨所有螢幕）無邊框、置頂、透明背景視窗。
- `DwmExtendFrameIntoClientArea(-1)` + 相機背景色 alpha = 0，達成逐像素透明。
- **點擊穿透**：每幀（或每 3 幀）讀取滑鼠位置下的像素 alpha（或用角色 Collider 射線檢測）；在角色身上時取消 `WS_EX_TRANSPARENT` 讓角色可被點擊，否則加上 `WS_EX_TRANSPARENT` 讓滑鼠穿透到底下的應用程式。
- 不在工作列顯示主視窗，改用系統匣（Tray）圖示。
- 支援多螢幕、不同 DPI 縮放（Per-Monitor DPI Aware v2）。
- 全螢幕遊戲/簡報偵測：前景視窗為全螢幕時自動隱藏或縮到角落（可設定）。

### 4.2 環境感知（DesktopEnv）

| 資料 | 取得方式 | 用途 |
|---|---|---|
| 工作列位置與高度 | `SHAppBarMessage` | 角色預設站在工作列上方 |
| 可見視窗矩形 | `EnumWindows` + `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` | 角色可以跳上視窗頂端、坐在視窗邊緣 |
| 視窗 Z 順序與移動 | `SetWinEventHook`（位置變更事件） | 視窗被拖走時角色會掉下來 |
| 滑鼠位置 | `GetCursorPos` | 角色視線跟隨滑鼠、追逐游標 |
| 使用者閒置時間 | `GetLastInputInfo` | 閒置太久角色打瞌睡 |

### 4.3 行為系統（BehaviorAI）

採 **階層式狀態機 + 效用（Utility）評分** 決定下一個行為：

| 狀態 | 觸發條件 | 動畫 |
|---|---|---|
| Idle 待機 | 預設 | 呼吸、眨眼、左顧右盼、伸懶腰（隨機） |
| Walk 走路 | 隨機目的地 | 依距離選走/跑，含轉身 |
| Climb / Jump | 目的地在視窗頂端 | 跳躍、攀爬 |
| Sit 坐下 | 站在視窗邊緣 | 雙腳垂下晃動 |
| Sleep 睡覺 | 使用者閒置 > N 分鐘 | 打哈欠 → 躺下 |
| Dragged 被拖曳 | 使用者按住角色 | 懸空掙扎（布偶物理） |
| Fall 掉落 | 放開或腳下視窗消失 | 自由落體 → 落地緩衝 |
| Listen 聆聽 | 使用者開始輸入/說話 | 轉向使用者、點頭 |
| Think 思考 | 等待 AI 回覆 | 手托下巴、頭上冒問號 |
| Talk 說話 | AI 回覆中 | 口型同步 + 回答內容對應的動作 |
| React 互動 | 被點擊、被戳 | 依點擊部位反應（頭：害羞、肩：回頭） |

- 移動採用 **Root Motion**，腳步不滑動；地面為工作列/視窗頂端組成的「平台」。
- 動畫使用 Unity Humanoid（Mecanim），任何 VRM 角色都可共用同一套動畫。
- 頭髮、裙擺使用 VRM SpringBone 物理；被拖曳時使用 Ragdoll 或 Active Ragdoll。
- 視線（LookAt）與頭部 IK 跟隨滑鼠或說話對象。

### 4.4 動畫素材來源

| 來源 | 授權注意 |
|---|---|
| 自製（Blender） | 最安全 |
| 動作捕捉（如 Rokoko、手機 AI 動捕） | 依服務條款 |
| Mixamo | 可用於自己的作品中，但**不可單獨散布動畫檔**，需打包在程式內 |
| Unity Asset Store 動畫包 | 依各包授權（通常允許內嵌散布） |

第一版至少需要 **40 個基礎動畫**（待機 ×5、走/跑/轉身 ×6、跳/爬/落 ×5、坐/躺/睡 ×5、情緒動作 ×12、手勢 ×7）。

### 4.5 效能預算

| 項目 | 目標 |
|---|---|
| 閒置時 CPU | < 3%（4 核心以上 CPU） |
| 記憶體 | < 500 MB |
| 幀率 | 活動時 60 fps；閒置/睡覺時降到 20 fps |
| GPU | 內顯（Intel Iris Xe 等級）可流暢執行 |
| 筆電使用電池時 | 自動省電模式（30 fps、關閉陰影） |

---

## 5. AI 代理人模組

### 5.1 支援的 AI 服務

| 服務 | API 類型 | 備註 |
|---|---|---|
| ChatGPT（OpenAI） | OpenAI Chat Completions / Responses API | |
| Claude（Anthropic） | Anthropic Messages API | |
| DeepSeek | OpenAI 相容 API | 只需換 Base URL |
| Gemini（Google） | Gemini API（亦提供 OpenAI 相容端點） | |
| Grok（xAI） | OpenAI 相容 API | |
| 本地模型 | Ollama / LM Studio / llama.cpp server / vLLM（皆 OpenAI 相容） | 預設 `http://localhost:11434` 等，免金鑰 |
| 自訂 | 任何 OpenAI 相容端點 | 使用者填 Base URL + Key |

**模型清單不寫死在程式裡**：綁定後呼叫各家的模型列表 API 動態取得，使用者在下拉選單中選擇；另提供「手動輸入模型名稱」欄位，以便新模型上市時不必等 App 更新。

### 5.2 Provider 抽象介面

```csharp
public interface IChatProvider
{
    string Id { get; }                       // "openai", "anthropic", "ollama"...
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);
    IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest req, CancellationToken ct);
    ProviderCapabilities Capabilities { get; } // 是否支援工具呼叫、圖片輸入、JSON 模式
}

public record ChatRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<ChatMessage> History,
    IReadOnlyList<ToolDef>? Tools,
    float? Temperature,
    int? MaxOutputTokens);
```

- 每個 Provider 只負責「格式轉換 + HTTP 串流」，其餘（記憶、路由、動作解析）由共用層處理。
- 所有請求支援取消（使用者按「停止」或打斷角色說話）。
- 錯誤處理：逾時、額度不足、金鑰錯誤、速率限制（429 自動退避重試）都要轉成角色的友善台詞（例如「我好像連不上 Claude，要不要檢查一下金鑰？」）。

### 5.3 多 AI 路由規則

使用者可綁定多個 AI，每次下指令時決定交給誰：

| 方式 | 範例 | 說明 |
|---|---|---|
| 預設 AI | （直接輸入） | 使用角色設定中的預設 AI |
| 下拉選單 | 輸入框左側選擇 | 選定後本次（或持續）使用 |
| @ 指定 | `@claude 幫我潤稿` | 臨時指定，不改預設 |
| 多選比較 | `@gpt @gemini 哪個好？` | 同時發送，角色依序念出或以多個泡泡並列，標示來源 |
| 自動路由（選配） | 設定規則 | 例如「寫程式 → Claude、需要搜尋 → Gemini、私密內容 → 本地模型」 |

**對話歷史**：每個 AI 各自維護上下文，或使用「共用上下文」模式（切換 AI 時把先前對話一併帶過去）；由使用者在設定中選擇。

### 5.4 回答 → 動作對應（ActionMapper）

**主要方式：要求 AI 回傳結構化結果。** 系統提示詞中附上角色可用的動作與表情清單，請 AI 以工具呼叫（Tool Calling）或 JSON 模式回傳：

```json
{
  "text": "好的！明天早上九點我會提醒你開會～",
  "emotion": "happy",
  "intensity": 0.7,
  "actions": [
    { "at": 0,  "name": "nod" },
    { "at": 12, "name": "thumbs_up" }
  ]
}
```

- `at`：在第幾個字時觸發動作，與 TTS 播放進度同步。
- `emotion` 白名單：`neutral, happy, sad, angry, surprised, shy, thinking, sleepy, excited, worried`
- `actions` 白名單：`nod, shake_head, wave, bow, thumbs_up, clap, shrug, point, facepalm, dance, jump, cheer, cry, laugh, think_pose`…（與 §4.4 動畫庫一一對應）
- 不在白名單內的動作一律忽略，避免異常。

**備援方式**（本地小模型不擅長結構化輸出時）：

1. 解析文字中的行內標記，例如 `[happy] 好的！[wave]`。
2. 若完全沒有標記，用本地輕量情緒分類（關鍵字規則 + 小型分類模型）推測情緒。
3. 預設動作：說話時自動加入隨機手勢與點頭，避免角色呆站。

**串流處理**：AI 回覆以串流方式到達，以「句子」為單位切段，第一句一到就開始 TTS 與動作，降低等待感（目標：送出到角色開口 < 1.5 秒，雲端模型首字延遲另計）。

### 5.5 角色人格與記憶

- **系統提示詞** = 角色人格設定（名字、個性、說話口吻）＋ 動作/表情規格 ＋ 使用者偏好（例如「用台灣繁體中文回答」）。
- **短期記憶**：最近 N 輪對話；超過 token 上限時自動摘要。
- **長期記憶（第二階段）**：重要事實（使用者名字、生日、喜好）存在本地 SQLite，必要時注入提示詞。
- 聊天紀錄存在本地，可匯出/清除。

### 5.6 輸入與輸出

| 輸入 | 說明 |
|---|---|
| 文字 | 點擊角色或按全域快捷鍵（預設 `Ctrl+Alt+Space`）叫出輸入框 |
| 語音 | 按住說話（Push-to-Talk）；選配：喚醒詞（例如喊角色名字） |
| 拖放 | 把檔案/圖片拖到角色身上 → 附加給支援的 AI（例如「幫我看這張圖」） |
| 剪貼簿 | 選配：「幫我翻譯剪貼簿內容」 |

| 輸出 | 說明 |
|---|---|
| 對話泡泡 | 跟隨角色頭頂、打字機效果、支援 Markdown（程式碼區塊、清單）；長回答可展開成側邊面板 |
| 語音 | TTS 播放 + 口型同步；可隨時打斷 |
| 動作/表情 | 見 §5.4 |
| 模式切換 | 僅文字 / 僅語音 / 兩者 |

---

## 6. 語音模組（VoiceService）

| 項目 | 離線選項 | 雲端選項 |
|---|---|---|
| TTS | Windows 內建語音（含台灣中文語音）、Piper | OpenAI TTS、Azure 語音（台灣口音）、ElevenLabs、Google TTS |
| STT | whisper.cpp（small/medium 模型，支援 GPU） | OpenAI Whisper/轉錄 API、Azure、Google |
| 口型 | uLipSync 即時分析音訊母音 → VRM 表情 `aa/ih/ou/ee/oh` | 若 TTS 回傳 viseme 時間軸則直接使用 |

- 每個角色可指定專屬聲音。
- 語音資料預設**不保存**，只在處理期間暫存在記憶體。

---

## 7. 設定與介面

- **系統匣選單**：顯示/隱藏角色、切換角色、開啟捏人、開啟設定、勿擾模式、結束。
- **設定頁**：
  - AI 帳號綁定（新增/測試連線/刪除，金鑰以 DPAPI 加密）
  - 預設 AI、路由規則、對話上下文模式
  - 語音（TTS/STT 引擎、音色、音量、語速）
  - 行為（活動頻率、是否爬視窗、全螢幕時隱藏、閒置多久睡覺）
  - 外觀（角色大小、透明度、始終置頂）
  - 快捷鍵、開機自動啟動、語言（第一版：繁體中文、英文）
- **聊天紀錄視窗**：依 AI 分類、搜尋、匯出。

---

## 8. 資料格式與儲存

```
%AppData%\AIDigitalDoll\
├─ settings.json                 一般設定（不含金鑰）
├─ secrets.bin                   DPAPI 加密的 API 金鑰
├─ characters\
│   └─ {角色ID}\
│       ├─ character.doll        捏人參數（可再次編輯）
│       ├─ model.vrm             烘焙後的執行期模型
│       ├─ thumbnail.png
│       └─ persona.json          人格、聲音、預設 AI
├─ chats\{角色ID}.sqlite         聊天紀錄與長期記憶
└─ logs\
```

`character.doll`（JSON）範例：

```json
{
  "formatVersion": 1,
  "assetVersion": "mh-cc0-2026.1",
  "base": "female",
  "macro": { "gender": 0.9, "age": 0.45, "muscle": 0.5, "weight": 0.48, "height": 0.55, "proportions": 0.6 },
  "modifiers": { "face/eye-size": 0.35, "torso/breast-size": 0.2, "hip/hip-width": -0.1 },
  "appearance": { "skin": "skin_light_02", "eyeColor": "#5B3A29", "hair": "long_wavy", "hairColor": "#2B1B12" },
  "outfit": [ { "id": "shirt_basic", "color": "#FFFFFF" }, { "id": "skirt_pleated", "color": "#1F2A44" } ]
}
```

- 存檔只記錄**參數**，檔案極小，可分享；`model.vrm` 可由參數重新產生。
- 有 `formatVersion` 以便未來升級時自動遷移。

---

## 9. 非功能需求

| 類別 | 需求 |
|---|---|
| 安全 | API 金鑰 DPAPI 加密；日誌中遮蔽金鑰；所有雲端連線走 HTTPS；不蒐集任何遙測（或預設關閉並可選擇開啟） |
| 隱私 | 對話只送給使用者選擇的 AI；明確提示「此訊息將傳送到 ○○ 服務」；本地模型模式完全離線 |
| 穩定性 | AI 或語音服務失敗不得讓角色當機；Unity 例外統一記錄 |
| 相容性 | 支援 VRM 0.x / 1.0 匯入；Windows 10/11；多螢幕與 100%–200% DPI |
| 防毒誤判 | 程式碼簽章（Code Signing 憑證）；避免使用鍵盤全域 Hook（快捷鍵改用 `RegisterHotKey`） |
| 可維護性 | 模組以介面隔離；AI Provider、TTS、STT 皆可外掛化 |
| 在地化 | 字串外部化（Unity Localization），第一版繁中/英文 |

---

## 10. 內容政策與授權注意事項

1. **成人內容**：MakeHuman 素材中與性器官相關的 target 與素材，在轉檔階段直接排除；服裝不提供可脫到裸體的選項（最低限度保留基本內衣層）。這也是各家 AI 服務條款與未來上架（Microsoft Store 等）的要求。
2. **AI 服務條款**：各家 API 有使用政策，App 需在綁定頁提示使用者自行遵守；不得以 App 規避各家安全機制。
3. **第三方素材授權清冊**：建立 `THIRD_PARTY_NOTICES.md`，記錄每一項素材（MakeHuman CC0、社群 CC-BY 素材的作者標示、動畫來源、字型、套件授權）。
4. **AGPL 隔離**：專案中不得出現任何 MakeHuman 的 Python 程式碼或其逐行翻譯；轉檔工具自行撰寫。
5. **肖像權**：提供「依照片捏臉」功能（未來）時，需提示使用者不得冒用他人肖像。

---

## 11. 開發里程碑

先打通「桌面角色 + AI 對話」主線，讓產品儘早可用；捏人系統較複雜，並行開發。POC 階段先用 **VRoid Studio 匯出的 VRM** 或 **MakeHuman 匯出的 FBX（方案 A）** 當角色。

| 階段 | 內容 | 預估時間（1–2 人） | 驗收標準 |
|---|---|---|---|
| **M0 技術驗證** | Unity 透明置頂視窗、點擊穿透、載入 VRM、站在工作列上走路 | 2 週 | 角色在桌面走動，滑鼠可點到後方程式 |
| **M1 桌面行為** | 行為狀態機、拖曳/掉落、爬視窗、睡覺、多螢幕、系統匣 | 3–4 週 | 連續執行 8 小時無當機，閒置 CPU < 3% |
| **M2 AI 對話** | Provider 抽象層、6 種服務、金鑰加密、對話泡泡、串流、@ 路由 | 3 週 | 6 種服務都能對話；可同時問兩個 AI |
| **M3 動作與語音** | ActionMapper、情緒表情、TTS、口型、STT、40 個動畫 | 4 週 | 回答時動作與內容相符率 > 80%（人工評估 50 題） |
| **M4 捏人系統（身體）** | MakeHuman 素材轉檔工具、C# 變形引擎、骨架重算、捏人 UI、VRM 匯出 | 8–10 週 | 全部部位可調、滑桿即時（< 16 ms）、匯出 VRM 可在桌面模式使用 |
| **M5 服裝與外觀** | MHCLO 貼合、髮型、膚色/妝容、服裝素材包（首批 30 件） | 4–6 週 | 改變身材時衣服自動貼合，無明顯穿模 |
| **M6 打磨與發佈** | 長期記憶、設定頁完善、安裝包、程式碼簽章、繁中/英文、Beta 測試 | 3–4 週 | 安裝到全新 Windows 可直接使用 |

**總計約 7–9 個月**（1–2 位開發者）。若有 3D 美術協助（衣服、動畫），M4–M5 可明顯縮短。

---

## 12. 風險評估

| 風險 | 影響 | 機率 | 對策 |
|---|---|---|---|
| 捏人變形引擎複雜度超出預期 | 高 | 中 | 先上線 VRM 匯入與方案 A；變形引擎分階段（先 macro，再臉部，再身體細節） |
| MakeHuman 寫實畫風不符使用者期待 | 中 | 中 | 提供 Toon Shader 與動漫風 preset；保留 VRM 匯入（VRoid）路線 |
| 衣服素材不足 | 中 | 高 | 自製 + 收錄 CC0 社群素材；規劃衣服製作流程文件 |
| AI 回傳動作格式不穩定（尤其本地小模型） | 中 | 高 | 白名單 + 三層備援（§5.4） |
| 透明視窗在部分顯示卡/遠端桌面異常 | 中 | 低 | 提供「一般視窗模式」備援 |
| 防毒軟體誤判（置頂、點擊穿透） | 中 | 中 | 程式碼簽章、避免可疑 API |
| 雲端 API 費用 | 低 | 中 | 顯示 token 用量估算；支援本地模型 |

---

## 13. 待決事項（需要您確認）

1. **畫風方向**：寫實（MakeHuman 原生）、動漫風，還是兩者都要？這會影響 Shader、預設範本與衣服的製作方向。
2. **商業模式**：免費開源、買斷、還是有付費素材？會影響授權策略（例如程式本身是否開源、要不要上架 Microsoft Store）。
3. **AI 費用**：由使用者自備 API 金鑰（建議第一版採用），或未來提供代付方案？
4. **語言**：介面與預設語音只要繁體中文，還是也要英文、日文？
5. **團隊**：目前開發人數與是否有 3D 美術人力？這會決定時程是否可壓縮。

---

## 附錄 A：名詞解釋

| 名詞 | 說明 |
|---|---|
| Target | 一組「頂點位移量」，套用在基礎網格上可改變形狀（例如「鼻子變高」） |
| Modifier | 使用者看到的滑桿，背後控制一個或多個 target 的權重 |
| Blendshape | 引擎中的變形目標，常用於表情 |
| VRM | 以 glTF 為基礎的 3D 人形角色開放格式，廣泛用於 VTuber |
| MHCLO | MakeHuman 衣服格式，衣服頂點綁定在身體頂點上，可隨身形自動貼合 |
| Root Motion | 由動畫本身驅動角色位移，避免腳步滑動 |
| SSE | Server-Sent Events，AI 回覆串流的傳輸方式 |
| DPAPI | Windows 內建的資料加密 API，以目前使用者帳號為金鑰 |
