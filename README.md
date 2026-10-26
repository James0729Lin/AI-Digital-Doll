# AI Digital Doll（AI 桌面數位人偶）

一款免費、開源（MIT）的 Windows 桌面應用程式：

1. **捏人系統**：寫實或動漫風，從性別開始自訂 3D 人物的臉型、五官、身材（胸、臀、腿、手臂等）與服裝，並存檔。
2. **桌面人偶**：捏好的人物以 3D 角色出現在 Windows 桌面上，自由走動、坐在視窗上、做出各種動作。
3. **AI 代理人**：綁定 ChatGPT / Claude / DeepSeek / Gemini / Grok 或本地模型（使用者自備 API 金鑰），對人偶下指令，由人偶轉交給 AI，再用語音或對話泡泡回答，並**依每則回答的內容演出不同的動作與表情**。

介面語言：繁體中文、English。

## 下載與使用（一般使用者）

1. 到本頁右側的 **[Releases](../../releases)**，下載最新版的 `AIDigitalDoll-…-windows-x64.zip`。
2. 解壓縮到任意資料夾（**整個資料夾都要保留**），執行 `AIDigitalDoll.exe`。
3. 程式尚未數位簽章，若出現「Windows 已保護您的電腦」，請點「其他資訊」→「仍要執行」。
4. 角色會出現在工作列上方。**雙擊角色**可以和她聊天（要先在「AI 設定」綁定一個 AI），點一下是戳她，按住拖曳可以拎起來，游標在頭上來回移動是摸頭。
5. 在角色身上按 **右鍵** 開啟選單：
   - **聊天…** / **AI 設定…**：綁定 ChatGPT、Claude、Gemini、DeepSeek、Grok 或本地模型（Ollama、LM Studio），見 [AI 聊天測試說明](docs/TESTING-AI.md)
   - **捏身材…**：自訂身材與五官，按「回到桌面」後桌面角色就會換成您捏的樣子
   - **載入 VRM 角色…**：使用 VRoid Studio 等工具做的 `.vrm` 角色（也可以直接把檔案拖到角色身上）
   - **結束**：關閉程式

系統需求：Windows 10 / 11（64 位元）、支援 OpenGL 3.3 的顯示卡。

## 目前進度

**M0 技術驗證**（進行中）：透明桌面視窗、滑鼠穿透、執行期載入 VRM 角色、程序化待機/走路/拖曳/掉落動作、系統匣選單。
測試方式見 [M0 Windows 測試說明](docs/TESTING-M0.md)。

**動物模式**：右鍵選單可換成小貓、小狗、小雞、小豬，一樣會走動、爬視窗、睡覺、被摸、聊天。

**互動與 AI 聊天**（M2，進行中）：戳、摸頭、搓揉的反應，躺下打瞌睡（裙子與頭髮會順著重力垂下）、坐在地上、跳舞、偶爾小跑步、摔倒趴地、刻意跳下時雙腳著地、待機小動作；串接 AI（使用者自備金鑰），回答時依內容做出不同表情與手勢。測試方式見 [AI 聊天測試說明](docs/TESTING-AI.md)。

**捏身材原型**（已完成）：MakeHuman CC0 素材、575 種變形、胸／腰／臀／屁股／手臂／腿等 100 多個滑桿（簡易／進階、左右對稱）、素體＋貼身內衣、即時預覽、存檔、復原／重做。
捏好的身體已完成骨架綁定，可直接成為桌面角色（走路、拎起、掉落）。
測試方式見 [捏身材測試說明](docs/TESTING-BODY.md)。

## 文件

- [系統規格書](docs/SPEC.md)
- [M0 Windows 測試說明](docs/TESTING-M0.md)
- [捏身材測試說明](docs/TESTING-BODY.md)

## 專案結構

```
app/                    Godot 4.7 (.NET) 專案
  addons/               godot-vrm、MToon Shader（MIT，第三方）
  assets/               內建角色與圖示
  scenes/               場景
  src/Body/             素體網格顯示
  src/Creator/          捏身材畫面
  src/Character/        VRM 載入、骨架姿勢、表情、程序化動畫
  src/Mascot/           桌面模式主程式
  src/Platform/         Win32 呼叫
  locale/               介面翻譯（zh_TW、en）
src/DigitalDoll.Core/   不依賴 Godot 的核心邏輯（可單元測試）
tests/                  單元測試
tools/                  建置與檢查工具
  makehuman/convert.py  MakeHuman CC0 素材轉檔
```

## 從原始碼建置（開發者）

需要 [.NET 8 SDK](https://dotnet.microsoft.com/) 與 [Godot 4.7.2 .NET 版](https://godotengine.org/download)。
所有素材（MakeHuman CC0 轉檔結果、字型、範例角色）都已在原始碼庫中，複製下來即可建置，不需另外下載。

```bash
dotnet test tests/DigitalDoll.Core.Tests      # 單元測試
cd app && godot --headless --import            # 匯入資源
godot --path app                               # 執行

# 開發用：算出某個姿勢的截圖後結束
godot --path app -- --snapshot out.png --pose walk --time 1.2 --yaw 1.57
godot --path app -- --creator --snapshot out.png --view side --set breast-size=1 --tab chest
godot --path app -- --selftest                 # 桌面 → 捏身材 → 桌面 自動切換測試
```

每次推送，GitHub Actions 都會執行單元測試、匯出 Windows 版，並用打包後的程式算圖做冒煙測試。

**發佈新版**：在 GitHub 的 Actions → Build → Run workflow，於 `release` 欄位輸入版本號（例如 `v0.2.0`），
或推送 `v*` 標籤，建置完成後會自動建立 Release 並附上 Windows 版 zip。

## 授權

程式碼：MIT（見 [LICENSE](LICENSE)）。第三方元件與素材授權見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
