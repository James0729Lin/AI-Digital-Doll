# AI Digital Doll（AI 桌面數位人偶）

一款免費、開源（MIT）的 Windows 桌面應用程式：

1. **捏人系統**：寫實或動漫風，從性別開始自訂 3D 人物的臉型、五官、身材（胸、臀、腿、手臂等）與服裝，並存檔。
2. **桌面人偶**：捏好的人物以 3D 角色出現在 Windows 桌面上，自由走動、坐在視窗上、做出各種動作。
3. **AI 代理人**：綁定 ChatGPT / Claude / DeepSeek / Gemini / Grok 或本地模型（使用者自備 API 金鑰），對人偶下指令，由人偶轉交給 AI，再用語音或對話泡泡回答，並**依每則回答的內容演出不同的動作與表情**。

介面語言：繁體中文、English。

## 目前進度

**M0 技術驗證**（進行中）：透明桌面視窗、滑鼠穿透、執行期載入 VRM 角色、程序化待機/走路/拖曳/掉落動作、系統匣選單。
測試方式見 [M0 Windows 測試說明](docs/TESTING-M0.md)。

## 文件

- [系統規格書](docs/SPEC.md)
- [M0 Windows 測試說明](docs/TESTING-M0.md)

## 專案結構

```
app/                    Godot 4.7 (.NET) 專案
  addons/               godot-vrm、MToon Shader（MIT，第三方）
  assets/               內建角色與圖示
  scenes/               場景
  src/Character/        VRM 載入、骨架姿勢、表情、程序化動畫
  src/Mascot/           桌面模式主程式
  src/Platform/         Win32 呼叫
  locale/               介面翻譯（zh_TW、en）
src/DigitalDoll.Core/   不依賴 Godot 的核心邏輯（可單元測試）
tests/                  單元測試
tools/                  建置與檢查工具
```

## 開發

需要 [.NET 8 SDK](https://dotnet.microsoft.com/) 與 [Godot 4.7.2 .NET 版](https://godotengine.org/download)。

```bash
dotnet test tests/DigitalDoll.Core.Tests      # 單元測試
cd app && godot --headless --import            # 匯入資源
godot --path app                               # 執行

# 開發用：算出某個姿勢的截圖後結束
godot --path app -- --snapshot out.png --pose walk --time 1.2 --yaw 1.57
```

每次推送，GitHub Actions 都會執行單元測試、匯出 Windows 版，並用打包後的程式算圖做冒煙測試；Windows 版可在 Actions 的 Artifacts 下載。

## 授權

程式碼：MIT（見 [LICENSE](LICENSE)）。第三方元件與素材授權見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
