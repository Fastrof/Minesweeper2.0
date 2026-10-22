# 扫雷 2.0

**简体中文** | [English](README.en.md)

<p align="center">
  <img src="docs/images/icon-256.png" width="176" alt="扫雷 2.0 图标">
</p>

一个从零手写的 Windows 扫雷：**先把经典玩法 100% 还原，再往上加新的东西**。

> 技术栈 **C# + .NET 10 + WinUI 3（Windows App SDK）+ Win2D**，不需要 Visual Studio，`dotnet build` 即可编译。

| | |
|---|---|
| **当前版本** | `v1.5.1` |
| **构建** | `build.cmd` → **0 警告 0 错误** |
| **测试** | `test.cmd` → **389 / 389 通过**（约 1 秒） |
| **仓库** | https://github.com/Fastrof/Minesweeper2.0 |
| **商店** | [Microsoft Store](https://apps.microsoft.com/detail/9NKR2M2ZDDL3) |
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
- **三个道具**：放大镜（透视 2×2，点中的格子是这块区域的左上角）、声呐扫描（只报 3×3 里有几颗雷）、
  时间冻结（暂停倒计时 20 秒）。首次进入每种各送 2 个，持有上限 9 个。
- **可调主题**：19 个颜色键组成的调色板，内置多套配色。
- **代码合成音效**：翻开 / 插旗 / chord / 踩雷 / 胜利 / 启动，共 6 个。
- **缩放平移与远景降级**：单局最大 **4096×4096**；100 万格同屏渲染实测约 **70 FPS**。
- **无障碍**：自绘棋盘的 UI Automation 支持，屏幕阅读器可朗读盘面。
- **成就系统（v1.1）**：31 条成就、合计 1000 点，分累计 / 速度 / 关卡 / 道具 / 挑战五类，其中 2 条是隐藏成就。
- **检查更新（v1.1）**：启动时静默检查 + 设置里手动检查，可选**自动下载**安装包；商店版不检查（由 Microsoft Store 负责更新）。
- **更新设置（v1.2）**：自动检查频率（每次启动 / 每天 / 每 3 天 / 每周 / 每 2 周 / 每月）、可**暂停更新**并选到哪天为止；升级后首次打开自动展示「本版新增」。
- **中英双语（v1.3）**：界面与全部文案（含 Toast、两个「关于」窗口、关卡/道具/成就/更新面板）支持**简体中文 / English 实时切换**，首次安装默认简体中文。
- **界面颜色主题（v1.3）**：**跟随系统 / 浅色 / 深色**；深色下自动保证**棋盘与窗口背景分得开**（按亮度差自动微调棋盘底色，高对比主题同样适用）。
- **金币商店（v1.4 / v1.5.1 起跨局）**：棋盘右上角有「🛒 商店」，扫雷过程中随时能点开。每扫出一颗雷得 **1 金币**；
  放大镜 **30** 金币、声呐扫描 **20**、时间冻结 **20**。金币**跨局累计**（新局 / 新关卡不再清零），
  商店买到的道具直接进入物品栏，也跨局保留。
- **无猜模式（v1.5）**：设置里新增「无猜模式」开关（默认关）。打开后每张盘都保证能靠纯逻辑推完，
  **不用赌二选一**；首点安全区、同种子同首点仍是同一张盘的规则都不变，
  无猜盘的分享串会多带一节标记（旧分享串仍然可以复现）。
- **提示（v1.5）**：普通局状态栏新增「💡 提示」，点一下高亮一个确定安全的格子（不自动翻开，首点后可用）；
  用过提示的局不计最快记录（会弹出说明）；无猜局与关卡局不显示这个按钮。
- **金币跨局累计（v1.5.1）**：赚到的金币不再每局清零；商店买到的道具直接进账号物品栏，也跨局保留；旧存档自动迁移（金币按 0 起算）。
- **发布页视觉重做（v1.5）**：官网换成纸色格线、凸起按钮与商店徽章。

## 平台支持

| 平台 | 状态 | 版本 | 获取方式 |
|---|---|---|---|
| Windows 10/11 | 主力版本 | v1.5.2 | 本仓库 Release 的 MSIX / 便携包 |
| Linux (x64) | v1.5 起维护 | v1.5.2-Linux | `linux/` 目录自行构建，或 Release 里的 `.deb` |

两个平台**共用同一套 Core**（规则、关卡、道具、成就、存档格式完全一致），只是 UI 层不同：

- Windows：WinUI 3 + Win2D —— 代码在 `src/`
- Linux：Avalonia + SkiaSharp —— 代码在 `linux/`，以**链接编译**的方式复用同一份 `MineSweeper.Core`（不复制、不分叉）

几点约定：

- Linux 端界面上的版本号统一带 `-Linux` 后缀（如 `v1.5.2-Linux`），避免玩家报版本时分不清平台。
- 发布 tag 也分开：Windows 用 `v1.5.2`，Linux 用 `linux-v1.5.2`；Linux 端的「检查更新」只认 `linux-v*` 前缀。
- Linux 的构建、运行、打包步骤见 [`linux/README.md`](linux/README.md)。

## 快速开始

```bat
git clone https://github.com/Fastrof/Minesweeper2.0.git
cd Minesweeper2.0

build.cmd     :: 编译整个解决方案（x64），0 警告 0 错误
run.cmd       :: 启动游戏
test.cmd      :: 跑 Core 层单测（389 个）
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
| `achievements.json` | 成就与成就点（v1.1） |
| `updatestate.json` | 检查更新的状态：上次检查时间 / 已提示版本 / 已跳过版本（v1.1） |
| `config.json` | 道具与棋盘配置（可手改，不用重编译）；v1.4 起带 `schemaVersion: 2`、放大镜宽高与商店价格键（`shop.*.price`） |
| `app.log` | 诊断日志 |
| `sounds\` | 首次运行合成出来的音效；**已存在的不覆盖**，想换掉直接放同名 wav |

所有存档都是**原子写盘**（含可手改的 `config.json`）：损坏会备份并重建；`schemaVersion` 过新时不会覆盖原文件（拒绝写回）。

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
linux/                      Linux 端（Avalonia + SkiaSharp），链接编译同一份 Core，不复制代码
  src/MineSweeper.App.Linux/
  packaging/                .deb 打包脚本 + desktop / AppStream metainfo
  scripts/                  build.sh / run.sh / 无头截图
tests/
  MineSweeper.Core.Tests/   389 个 xUnit 用例（21 个测试文件）
```

> **铁律：游戏规则不要写进 App 层。** 判断标准很简单 —— "这段逻辑能不能不启动 UI 就测"？
> 能测就该放 Core；Core 里连 `Windows.*` / `Microsoft.UI.*` 都不许出现。

## 测试

**389 个用例全部通过**（21 个测试文件，约 1 秒；逐文件分配以 `test.cmd` 输出为准）。

覆盖范围（按文件归类）：

- `LevelTests.cs` — 关卡表守门人、星级、解锁、存档、前后跳转
- `NoGuessTests.cs` — 无猜模式：三条推理规则、同种子同盘、重抽上限与退回普通盘、提示接口、7 节分享串
- `AchievementTests.cs` — 成就目录守门人、判定边界、隐藏成就、进度只增不减
- `UpdateTests.cs` / `UpdatePreferencesTests.cs` / `UpdateDownloadTests.cs` — 版本比较、更新频率与暂停、选包与 SHA256 校验
- `ThemeAndAudioTests.cs` / `UiThemeTests.cs` — 调色板、音效合成、界面颜色主题与棋盘对比度
- `ItemTests.cs` / `AreaShapeAndCoinTests.cs` — 道具与金币、商店购买、放大镜 2×2 的区域形状
- `LocalizationTests.cs` — 中英语言表完整性与实时切换
- `BoardGenerationTests.cs` / `FloodFillTests.cs` / `ChordTests.cs` / `WinConditionTests.cs` — 布雷、洪水填充、chord、胜负判定
- `AdvancedFeatureTests.cs` / `LockedFirstClickTests.cs` — 锁定盘面、透视迷雾、信息面板
- `PersistenceTests.cs` / `HistoryTests.cs` — 原子写盘、损坏降级、记录与历史累计
- `RegressionTests.cs` / `NotificationTests.cs` — 已修缺陷的回归防线与通知分级

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

**仅供学习与交流使用。**
