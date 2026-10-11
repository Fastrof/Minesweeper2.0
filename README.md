# 扫雷 2.0 / MineSweeper 2.0

**简体中文** | [English](README.en.md)

<p align="center">
  <img src="docs/images/icon-256.png" width="176" alt="扫雷 2.0 图标">
</p>

一个从零手写的 Windows 扫雷：**先把经典玩法 100% 还原，再往上加新的东西**。

> 严格按《扫雷 2.0-开发方向文档 v1.2》实现。
> 技术栈 **C# + .NET 10 + WinUI 3（Windows App SDK）+ Win2D**，不需要 Visual Studio，`dotnet build` 即可编译。

| | |
|---|---|
| **当前版本** | `v1.0.0`（首个公开发布版） |
| **构建** | `build.cmd` → **0 警告 0 错误** |
| **测试** | `test.cmd` → **162 / 162 通过**（约 1 秒） |
| **仓库** | https://github.com/Fastrof/Minesweeper2.0 |
| **许可** | 仅供学习与交流使用 |

---

## 这个工程里没有一张第三方图片、也没有一个第三方音频文件

主题是**纯调色板矢量自绘**，音效由**代码实时合成**，程序图标是自绘的多尺寸 ICO。

这不只是洁癖 —— 它直接回应了文档的版权红线：**本工程不含任何来自 Windows 7 扫雷的素材**。

## 还原的部分

左键翻开 · 右键三态（旗 → 问号 → 空）· 双击 / 中键 / 左右键同按 chord · 首点保证安全 ·
计时从第一次翻开开始 · 剩余雷数 · 失败时展示全部雷位。

## 新加的部分

- **关卡模式**：6 章 × 6 关 = **36 关**。每关一张固定盘 + 按用时评星（★★★ / ★★ / ★），逐关解锁；
  通关弹结算卡片，游戏内有「◀ 上一关 / 下一关 ▶」可以连着打。
- **三个道具**：放大镜（透视 3×3）、声呐扫描（只报 3×3 里有几颗雷）、时间冻结（暂停倒计时 20 秒）。
  每扫 20 颗雷攒一次"三选一"补给。
- **可调主题**：19 个颜色键组成的调色板，内置多套配色。
- **代码合成音效**：翻开 / 插旗 / chord / 踩雷 / 胜利 / 启动，共 6 个。
- **缩放平移与远景降级**：单局最大 **4096×4096**；100 万格同屏渲染实测约 **70 FPS**。
- **无障碍**：自绘棋盘的 UI Automation 支持，屏幕阅读器可朗读盘面。

## 快速开始

```bat
git clone https://github.com/Fastrof/Minesweeper2.0.git
cd Minesweeper2.0

build.cmd     :: 编译整个解决方案（x64），0 警告 0 错误
run.cmd       :: 启动游戏
test.cmd      :: 跑 Core 层单测（162 个）
```

脚本会依次探测 `%ProgramFiles%\dotnet`、`%ProgramFiles(x86)%\dotnet`、`%DOTNET_ROOT%`、`D:\dotnet`，
都没找到就用 PATH 里的 `dotnet`。等价的手工命令：

```bat
dotnet build MineSweeper2.sln -p:Platform=x64
dotnet test tests\MineSweeper.Core.Tests\MineSweeper.Core.Tests.csproj
dotnet run --project src\MineSweeper.App\MineSweeper.App.csproj -p:Platform=x64
```

> **必须指定 `-p:Platform=x64`** —— WinUI 项目不能用默认的 AnyCPU。

编译产物是**自包含**的（`WindowsAppSDKSelfContained=true`），也可以直接双击运行：

```
src\MineSweeper.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MineSweeper2.exe
```

⚠️ **必须在原位置双击，别只把 exe 拷出来** —— 同目录还有一百多个 Windows App SDK / WinUI / Win2D
运行时 DLL，只拷 exe 会启动失败。要挪位置请把**整个文件夹**一起复制。

### 环境要求

| 项 | 版本 / 说明 |
|---|---|
| .NET SDK | **10.0.401**（实测） |
| Windows | 10 1809+ / 11 |
| Visual Studio | **不需要**（Windows App SDK 与 Windows SDK BuildTools 都走 NuGet） |
| NuGet 包 | Windows App SDK 2.5.1、Win2D 1.4.0、CommunityToolkit.Mvvm 8.4.2（版本集中在 `Directory.Packages.props`） |

## 存档在哪

**`%LOCALAPPDATA%\MineSweeper2\`** —— v0.6 起统一放这里，不再放在 exe 旁边；老版本首次启动会自动搬迁。

| 文件 | 内容 |
|---|---|
| `settings.json` | 玩家设置（主题、音量等） |
| `progress.json` | 跨局进度与道具库存 |
| `records.json` | 各难度的最快记录 |
| `levelprogress.json` | 关卡进度（通关 / 星数 / 最好用时 / 尝试次数） |
| `history.json` | 累计统计（从第一次扫雷起） |
| `config.json` | 道具与棋盘配置（可手改，不用重编译） |
| `app.log` | 诊断日志 |
| `sounds\` | 首次运行合成出来的音效；**已存在的不覆盖**，想换掉直接放同名 wav |

所有存档都是**原子写盘**：损坏会留备份重建，`schemaVersion` 过新则拒读且不覆盖原文件。

## 项目结构

```
MineSweeper.Core    纯 .NET 类库，零 UI 依赖 → 所有游戏规则都在这里，全部可单测
      ↑
MineSweeper.App     WinUI 3 + Win2D → 只负责显示与输入，不含游戏规则
```

```
src/
  MineSweeper.Core/         核心逻辑：布雷 / 洪水填充 / chord / 关卡 / 道具 / 存档 / 主题 / 音效合成
  MineSweeper.App/          WinUI 3 外壳：渲染、输入、面板、关于窗口
tests/
  MineSweeper.Core.Tests/   162 个 xUnit 用例
```

> **铁律：游戏规则不要写进 App 层。** 判断标准很简单 —— "这段逻辑能不能不启动 UI 就测"？
> 能测就该放 Core；Core 里连 `Windows.*` / `Microsoft.UI.*` 都不许出现。

## 测试

**162 个用例全部通过。**

| 文件 | 用例 | 覆盖 |
|---|---|---|
| `LevelTests.cs` | 35 | 关卡表守门人、星级、解锁、存档、通关判定、前后跳转 |
| `ThemeAndAudioTests.cs` | 23 | 主题调色板、设置存取、音效合成 |
| `ItemTests.cs` | 17 | 道具计数与发放 |
| `BoardGenerationTests.cs` | 15 | 布雷与参数边界 |
| `AdvancedFeatureTests.cs` | 14 | 锁定盘面、透视迷雾、信息面板 |
| `HistoryTests.cs` | 14 | 历史记录累计与损坏处理 |
| `RegressionTests.cs` | 13 | 已修缺陷的回归防线 |
| `PersistenceTests.cs` | 11 | 存档与记录（原子写盘 / 损坏 / 版本） |
| `WinConditionTests.cs` | 7 | 胜负判定 + 胜利自动插旗 |
| `ChordTests.cs` | 5 | chord 判定与"旗插错会炸" |
| `FloodFillTests.cs` | 5 | 洪水填充（含 1000×1000 全空盘不栈溢出） |
| `NotificationTests.cs` | 3 | 通知级别与"只入日志"维度 |

---

## 想继续开发？

架构说明、改动指南、诊断开关、代码约定、**完整变更记录**与发布清单，都在
**[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)**。

几个最常查的：

| 想做什么 | 去哪 |
|---|---|
| 改关卡数值（盘面 / 雷数 / 星级门槛） | `Core/Levels/LevelTable.cs`，**只改这一张表** |
| 加一套主题 | `Core/Themes/BoardPalette.cs` 加一条记录，下拉框自动多一项 |
| 加一个音效 | `SoundEffect` 枚举 + `WavSynth` 加波形，首次运行自动合成 |
| 排查问题 | `docs/DEVELOPMENT.md` §11.4 的 `SAOLEI_*` 诊断开关 |

## 许可

**仅供学习与交流使用。** 本工程不含任何来自 Windows 7 扫雷的素材。
