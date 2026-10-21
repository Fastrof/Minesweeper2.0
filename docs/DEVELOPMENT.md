# 开发参考 / Development Notes

这里是 **扫雷 2.0** 的开发文档：架构、改动指南、诊断开关、代码约定、完整变更记录与发布清单。
面向"要接手继续开发或排查问题"的人；只想了解或运行项目，请看仓库根目录的 [README](../README.md)。

> 本文件由原 README 拆分而来（v1.0.0），内容与原 README 的 §0–§12 一一对应，**没有删减**。
> 文中引用的 "§N" 都是本文件内部的章节号。

**目录**

- [1. 环境要求](#1-环境要求)
- [2. 构建 / 运行 / 测试](#2-构建--运行--测试)
- [3. 目录结构](#3-目录结构对应文档-13)
- [4. 通知（Toast）系统](#4-通知toast系统)
- [5. 主题与音效](#5-主题与音效都不需要素材文件)
- [6. 无障碍 / 触屏 / 关卡制 / 关于页面](#6-无障碍触屏关卡制关于页面与文档里的可选项)
- [7. 文档条目 → 代码位置](#7-文档条目--代码位置)
- [8. 文档里"待拍板"项的落地取值](#8-文档里待拍板项的落地取值)
- [9. 测试](#9-测试)
- [10. 实现决定与已知待办](#10-实现决定与已知待办)
- [11. 接手开发须知](#11-接手开发须知写给下一位开发者)
- [12. 发版清单（GitHub + Microsoft Store）](#12-发版清单github--microsoft-store)
- [附录 A. 完整变更记录](#附录-a-完整变更记录)

---

## 1. 环境要求

| 项 | 版本 / 说明 |
|---|---|
| .NET SDK | **10.0.401**（实测）。`build.cmd` / `test.cmd` 会依次探测 `%ProgramFiles%\dotnet`、`%ProgramFiles(x86)%\dotnet`、`%DOTNET_ROOT%`、`D:\dotnet`，都不在就用 PATH 里的 `dotnet` |
| Visual Studio | **不需要**。WinUI 3 + Win2D 用 `dotnet build` 即可编译（Windows App SDK 与 Windows SDK BuildTools 都走 NuGet） |
| Windows | 10 1809+ / 11 |
| NuGet 包 | Windows App SDK 2.5.1、Win2D 1.4.0、CommunityToolkit.Mvvm 8.4.2（版本集中在 `Directory.Packages.props`） |

---


## 2. 构建 / 运行 / 测试

```bat
build.cmd     :: 编译整个解决方案（x64），0 警告 0 错误
test.cmd      :: 跑 Core 层单测（360 个）
run.cmd       :: 启动游戏（unpackaged，存档在 %LOCALAPPDATA%\MineSweeper2）
```

等价的手工命令：

```bat
cd /d <本仓库目录>

dotnet build MineSweeper2.sln -p:Platform=x64
dotnet test tests\MineSweeper.Core.Tests\MineSweeper.Core.Tests.csproj
dotnet run --project src\MineSweeper.App\MineSweeper.App.csproj -p:Platform=x64
```

> WinUI 项目必须指定平台（本方案为 `x64`），不能用默认的 AnyCPU。

### 2.1 直接双击运行（不经过脚本）

编译产物是**自包含**的（`WindowsAppSDKSelfContained=true`），所以双击 exe 就能跑：

```
src\MineSweeper.App\bin\x64\Debug\net10.0-windows10.0.19041.0\MineSweeper2.exe
```

- **必须在原位置双击，别单独把 exe 拷出来。** 同目录还有一百多个 Windows App SDK / WinUI / Win2D
  的运行时 DLL，只拷 exe 会启动失败；想挪到别处就把**整个文件夹**一起复制。
- 首次运行会在 **`%LOCALAPPDATA%\MineSweeper2\`** 生成存档（`settings.json`、`progress.json`、`records.json`、
  `config.json`、`app.log`、`sounds\`）。路径由 `AppPaths` 统一给出，所以从哪儿双击都一致。
  （v0.6 起不再写在 exe 旁边的 `data\`；打包版用包的 `LocalFolder`。）
- 想要桌面快捷方式：右键 exe → 发送到 → 桌面快捷方式。exe 的图标已经是本工程的图标。
- 注意这个目录在 `bin\x64\Debug\` 下，**重新构建或清理会重建它**；Debug 版首次启动有 JIT，会稍慢一点。

---


## 3. 目录结构（对应文档 §1.3）

```
MineSweeper2.sln
├── build.cmd / test.cmd / run.cmd   # 一键编译 / 测试 / 运行
├── Directory.Build.props            # 编译约定（LangVersion / Nullable / 分析器）
├── Directory.Packages.props         # 中央包管理：所有 NuGet 版本在这里
├── MineSweeper2.0.ico               # 图标原图（单帧 256×252，是 Assets 里那份多尺寸 ICO 的来源；不参与构建）
├── src/
│   ├── MineSweeper.Core/            # 纯 .NET 类库，零 UI 依赖（可无 UI 全量回归）
│   │   ├── Cell.cs / CellFlags.cs   # §2.1 单格 2 字节：邻雷数 + 位标志
│   │   ├── Board.cs                 # §2.1 一维数组 + 邻格遍历（含热路径 Span 版本）
│   │   ├── GameLimits.cs            # §0.1 参数边界与合法性校验
│   │   ├── BoardGenerator.cs        # §2.2 首点后布雷（选择采样法 + 零分配邻雷数计算）
│   │   ├── FloodFill.cs             # §2.3 迭代式洪水填充 + 分帧 + 改动区间记录
│   │   ├── ChordSolver.cs           # §2.4 chord 判定与目标收集
│   │   ├── GameSession.cs           # §1.3 单局状态机 + 改动区间回传 + 分享串复现
│   │   ├── BoardSnapshot.cs         # §6.3 盘面快照与种子分享串
│   │   ├── PlayerProgress.cs        # §5.7 跨局进度（schemaVersion + 驼峰键名）
│   │   ├── IClock.cs                # 时钟抽象（测试注入 ManualClock）
│   │   ├── Notifications/
│   │   │   └── GameNotification.cs  # 通知级别 + 通知记录 + INotificationSink（零 UI 依赖）
│   │   ├── Levels/
│   │   │   ├── LevelDefinition.cs   # 关卡定义：固定盘 + 星级门槛 + 道具数（纯数据）
│   │   │   ├── LevelTable.cs        # 内置 6 章 36 关（**调难度只改这一张表**）
│   │   │   ├── LevelProgress.cs     # 关卡战绩与解锁进度（levelprogress.json 的数据模型）
│   │   │   └── LevelRules.cs        # 星级评定 / 逐关解锁 / 结算（纯函数）
│   │   ├── Themes/
│   │   │   ├── PaletteColor.cs      # #RGB/#RRGGBB/#AARRGGBB 解析（纯数据）
│   │   │   └── BoardPalette.cs      # 4 套内置调色板 + 逐项颜色覆盖（主题 = 一组颜色）
│   │   ├── Audio/
│   │   │   ├── SoundEffect.cs       # 5 个音效
│   │   │   └── WavSynth.cs          # 波形合成 → WAV 字节（零素材、纯数学）
│   │   ├── Settings/
│   │   │   └── AppSettings.cs       # 音效开关/音量 + 主题与覆盖
│   │   ├── Items/
│   │   │   ├── ItemIds.cs                  # v0.6 三种道具的稳定 Id / 显示名 / 说明
│   │   │   ├── SonarScanItem.cs            # v0.6 声呐扫描：只报 3×3 的雷数
│   │   │   └── TimeFreezeItem.cs           # v0.6 时间冻结：暂停计时 20 秒
│   │   │   ├── IItem.cs             # 道具接口 + 失败原因文案
│   │   │   ├── ItemConfig.cs        # §5.7 配置，JSON 键名就是 item.magnifier.*
│   │   │   ├── ItemInventory.cs     # §5.2 持有量、上限、本局购买（v1.4）
│   │   │   ├── MagnifierItem.cs     # §5 放大镜：2×2 透视（v1.4 起，点中格为左上角）
│   │   │   ├── MagnifierReveal.cs   # 透视结果 / 使用结果
│   │   │   ├── AreaShape.cs         # v1.4 区域形状：宽×高 + 锚点（放大镜 2×2 左上 / 声呐 3×3 居中）
│   │   │   ├── CoinWallet.cs        # v1.4 金币钱包：每颗雷 +1、价格、每局清零
│   │   │   └── ShopPurchaseResult.cs # v1.4 商店购买结果（买不起 / 满仓 / 本局结束…）
│   │   └── Persistence/
│   │       ├── AtomicFile.cs        # 原子写盘（progress.json 与 records.json 共用）
│   │       ├── ProgressStore.cs     # §6.1 损坏备份 + 版本过新拒读
│   │       ├── RecordsStore.cs      # §6.2 记录分表（classic / classicWithItem / custom）
│   │       ├── AppSettingsStore.cs  # settings.json（音效 + 主题），同样原子写 + 损坏降级
│   │       ├── LevelProgressStore.cs # levelprogress.json（关卡进度）
│   │       └── ItemConfigStore.cs   # config.json 读写
│   └── MineSweeper.App/             # WinUI 3 客户端
│       ├── App.xaml(.cs)            # 入口：注册/注销 Toast、未处理异常发 Error 通知
│       ├── MainWindow.xaml(.cs)     # 状态栏/物品栏/设置/计时器/关卡面板/分帧驱动
│       ├── AboutInfo.cs             # 关于页面的元信息（**GitHub 地址填这里**）
│       ├── AboutText.cs             # 关于游戏页的正文文案
│       ├── Views/BoardView.cs       # §1.5/§3 交互路由、缩放平移、瞄准态、精确重绘
│       ├── Views/BoardViewAutomationPeer.cs  # §3.2 自绘棋盘的无障碍节点（只读朗读）
│       ├── Views/BoardCellAutomationPeer.cs  # §3.2 虚拟格节点（可 Invoke，只为可见区生成）
│       ├── Views/AboutGameWindow.xaml(.cs)   # 关于游戏（固定尺寸小窗口，对齐 winver）
│       ├── Views/AboutAuthorsWindow.xaml(.cs)# 关于作者（同上）
│       ├── Views/AboutWindowChrome.cs        # 两个关于窗口共用的尺寸与"不可缩放/不可最大化"样式
│       ├── Rendering/
│       │   ├── BoardTheme.cs        # §4.1 配色（含经典数字八色）
│       │   ├── BoardViewport.cs     # 视口换算：可见行列区间、命中测试
│       │   ├── BoardRenderer.cs     # §4.5 分块绘制 + 远景合并 + 远景降级 + 热力图
│       │   └── RenderPerfProbe.cs   # M2 性能自检（真实设备上实测并导出截图）
│       ├── ViewModels/GameViewModel.cs  # CommunityToolkit.Mvvm（ObservableProperty/RelayCommand）
│       ├── Assets/
│       │   ├── MineSweeper2.0.ico   # 程序图标（9 帧：16/20/24/32/40/48/64/128/256）
│       │   ├── ToastIcon.png        # Toast 通知图标（96×96 PNG，unpackaged 下用 file:// 引用）
│       │   └── Authors/             # 作者头像（由根目录 1448×1448 原图缩到 256×256）
│       │       ├── Fastrof.png
│       │       └── Shinichi.png
│       ├── Services/AppPaths.cs     # §7.2 存档路径与存储装配
│       ├── Services/ToastNotifier.cs# 通知出口：Windows Toast + 级别映射 + 限流
│       ├── Services/SoundPlayer.cs  # 音效出口：首次合成 wav 到 %LOCALAPPDATA%\MineSweeper2\sounds\ 并播放
│       └── app.manifest             # DPI 感知
└── tests/MineSweeper.Core.Tests/    # xUnit；§8 的 10 条用例 + 回归 + 主题/音效 + 关卡 + 通知 + 成就/更新/双语 = 360 个测试（20 个测试文件）
```

> 运行时会额外生成（**不属于源码**，也不需要提交）：`%LOCALAPPDATA%\MineSweeper2\`
> —— 里面是 `settings.json`（玩家设置）、`progress.json`（跨局进度）、`records.json`（记录）、
> `config.json`（道具配置）、`levelprogress.json`（关卡进度）、`history.json`（历史记录）、`app.log`（诊断日志）、
> `sounds\`（合成出来的音效，可直接替换）、`perf\`（性能自检导出的对照图）。
> v0.6 起存档统一放在这里（不再放在 exe 旁边的 `data\`，首次启动会自动搬迁旧数据，见 §0.4.4）。

---


## 4. 通知（Toast）系统

游戏内**没有**提示条。所有要给玩家看的信息都由产生它的地方定级，再通过 `INotificationSink` 发出去，
当前实现是 Windows Toast（`AppNotificationManager` + 原生 toast XML）。

### 4.1 只弹关键通知（其余只写日志）

**规则：只有"结果"和"错误"弹窗；过程性提示一律不弹。**

一开始的做法是"凡是值得说一句的都弹一下"，结果换主题、开关设置、每开一局都弹一条，
通知中心很快被刷满，而其中大部分信息**在界面上本来就看得到**（主题换了看得见、新局开了看得见）。
所以通知模型里加了 `ShowToast` 维度：

| | 弹窗 | 只写 `%LOCALAPPDATA%\MineSweeper2\app.log` |
|---|---|---|
| **内容** | 一局的结果 + 出错的 + 会让你以为程序坏了的拒绝 | 设置变更、新局开始、状态提示 |
| **清单** | 胜利 · 踩雷失败 · 新纪录 · 关卡通关/失败 · 获得道具 · 透视完成 · 开局失败 · 重开失败 · 分享串/关卡无法开始 · 复制失败 · 未处理异常 · 旗子插错位置 · 放大镜用不了 · chord 旗数不符 · 重开/分享时机不对 · 性能自检结果 | `app.ready` · `settings.theme` · `settings.locked` · `game.new` · `game.custom` · `mode.custom` · `level.start` · `game.replay-*` · `share.opened` · `share.copied` · `record.not-improved` · `magnifier.aiming` · `magnifier.cancelled` · `view.fitted` · `board.reveal-blocked` |
| **数量** | 19 处 | 16 处 |

两个值得单独说明的取舍：

- **点了翻不开的格子**（已翻开 / 已插旗 / 问号）改为只写日志：原版扫雷对这种"点了没反应"
  本来就是静默的（§0 要求 100% 还原），而且点到自己刚翻开的格子是最常见的误操作。
- **"本局成绩（未超过纪录）"** 改为只写日志：没破纪录时状态栏已经写着用时，弹一条只是噪音；
  真正破纪录仍然会弹（那是奖励）。

实现在 `Core/Notifications/GameNotification`：`GameNotification.Quiet(...)` / `QuietInfo` / `QuietSuccess`
把 `ShowToast` 置为 false，`ToastNotifier.Publish` 见到它就只写日志、
**并且不占用限流额度**（安静通知不该把弹窗的额度吃掉）。

### 4.2 级别 → Toast 表现

级别现在只影响提示音（时长一律短时，见 §0.1 第 1 条），只有上面那 19 处会用到：

| 级别 | 触发场景 | Toast 表现 |
|---|---|---|
| **Info** | 视角复位、性能自检结果 | 短时（约 7 秒自动收起）、**静音** |
| **Success** | 胜利、获得道具、刷新纪录、关卡通关、复现盘面 | 短时、系统默认提示音 |
| **Warning** | 明确的拒绝（放大镜用不了、chord 旗数不符、重开或分享时机不对） | 短时、Reminder 音 |
| **Error** | 踩雷失败、关卡失败、开局失败、重开失败、存档异常、未处理异常 | 短时、**警报音**（`Notification.Looping.Alarm`，与最初版本一致；`loop="false"` 不会循环） |

每条通知的底部归因文字会写明级别（`提示 · 扫雷 2.0` / `错误 · 扫雷 2.0` …）。

**图标**：通知正文区左侧的大图标是程序图标（`appLogoOverride`，指向随 exe 一起输出的
`Assets\ToastIcon.png`）；通知**表头**（应用名 `MineSweeper2` 左边）那个小图标由系统按 AUMID 解析，
unpackaged 下改不动——原因、试过的办法与正式解法见 §6.8。

**限流**：按 `(Tag, 文案)` 去重，5 秒窗口内只弹一次；被压掉的条数会在下一次放行时合并成
"（期间另有 N 条同类提示已合并）"。没有这一步，"周围旗数与数字不符"这类提示会把通知中心刷满。

**验证方式**：`SAOLEI_NOTIFY=1` 启动会做一遍"以前会弹 Toast"的动作
（换主题 + 开关设置 + 新开一局），然后报告新增弹窗条数——实测是 **0 条弹窗 / 3 条只入日志**，
结果写进 `%LOCALAPPDATA%\MineSweeper2\app.log`（`[NOTIFYCHECK]` 前缀）。普通启动连"新局""已就绪"都不弹，
所以 `Toast 发送成功（首次）` 这行在纯启动时不会出现。

**两个已知平台问题（都已在代码里兜住）**

1. `AppNotificationManager.Register()` 在 **self-contained + unpackaged** 下会抛
   `COMException：Unable to load resource dll. Microsoft.WindowsAppRuntime.Insights.Resource.dll`。
   这是 Windows App SDK 的已知缺陷（[microsoft/WindowsAppSDK#6071](https://github.com/microsoft/WindowsAppSDK/issues/6071)），
   2.5.1 仍未修复（维护者 2026-09-18 回复"修复还没进发布分支"）。
   **实测结论：注册抛异常但 `Show()` 依然能正常发通知**，所以代码只在 `app.log` 记一笔，绝不因此放弃发送。
2. 刻意**不使用** `scenario="urgent"`：它会先弹一次系统"重要通知"授权框（"是否允许来自 MineSweeper2 的重要通知？"），
   玩家若选"以后再说"，最关键的失败通知反而可能收不到。级别改用"时长 + 提示音"区分。

兜底：即使通知完全发不出去，每条通知都会写进 `%LOCALAPPDATA%\MineSweeper2\app.log`，不会静默丢失。

---

### 4.3 所有通知共用一个槽位（否则会排队、"跟不上操作"）

Windows 的 Toast 是**排队**的：一条没消失，下一条就得等。默认每条通知各占一个独立槽位，
于是"chord 未执行"还挂在屏幕上的时候踩到雷，那条"踩雷失败"要等前一条自己收起来（约 7 秒）
才会出现、才会响铃 —— 玩家感觉通知慢半拍。更糟的是**积压**：旧版本玩几局就能在操作中心里
攒下一串（实测 11 条），之后所有新通知都排在它们后面。

两条措施：

1. **所有通知共用同一个 `Tag` + `Group`**（`ToastNotifier.NotificationSlotTag/Group` = `game` / `minesweeper2`）。
   系统对 Tag/Group 相同的通知执行**替换**，所以新通知会立刻顶掉旧的那条并响铃。
   通知只反映"刚刚发生了什么"，旧的那条本来就该让位。
2. **启动时清空本作遗留的通知**（`AppNotificationManager.RemoveAllAsync()`），把历史积压清掉。
   这个 API 只作用于本作的通知，不会动别的应用。

实测（`SAOLEI_TOASTBURST=1`）：间隔 400ms 连发 Warning + Error 两条，
操作中心里**只剩 1 条且是后发的那条** ✓。

---


## 5. 主题与音效（都不需要素材文件）

这两项**不需要随工程分发任何素材**。而且文档 §7.3 已经写明"从 Win7 扫雷提取的图片/音效仅限本地学习，
不得随发布版本分发"，所以那条路也不该走。

### 5.1 主题 = 一组颜色（不需要图片）

棋盘、数字、旗子、雷全都是 Win2D 矢量自绘（§1.2.1 ④），所以一套主题本质上就是一组颜色：

| 内置主题 | 说明 |
|---|---|
| `classic` 经典 | 与文档 §4.1 / §0 第 8 条一致的原始配色 |
| `dark` 深色 | 暗底 + 提亮后的数字色 |
| `highContrast` 高对比 | 纯黑底 + 白色网格 + 高亮数字，给视力受限的玩家 |
| `paper` 纸质 | 暖色米黄底，长时间看不刺眼 |

配置在 `%LOCALAPPDATA%\MineSweeper2\settings.json`：

```json
{
  "themeId": "dark",
  "themeOverrides": { "coveredFill": "#FF00FF" }
}
```

`themeOverrides` 是**逐项覆盖单个颜色**——只想换掉未翻开格的颜色就写一行，不必把整套调色板抄一遍。
颜色支持 `#RGB` / `#RRGGBB` / `#AARRGGBB`；数字八色用 `number1`..`number8` 覆盖；
写错的项会被丢掉，其余照常生效（不会因为手改坏一个字段就整盘变黑）。

架构上守住了"Core 零 UI 依赖"：调色板的**数据模型**在 `Core/Themes/`（纯 `byte` RGB + JSON，可单测），
App 层只负责映射成 `Windows.UI.Color` 交给渲染器。渲染器不再读全局静态色，而是接收一个 `BoardTheme` 实例。

### 5.2 音效 = 代码合成（不需要音频文件）

音效不是随工程分发的素材，而是**首次运行时由 `WavSynth` 合成**到 `%LOCALAPPDATA%\MineSweeper2\sounds\*.wav`：

| 文件 | 合成方式 | 时长 |
|---|---|---|
| `reveal.wav` | 880Hz 短促正弦 | 45 ms |
| `flag.wav` | 1320Hz 方波 | 35 ms |
| `chord.wav` | 660→990Hz 双音上行 | 93 ms |
| `explosion.wav` | 噪声 + 220→55Hz 下滑 | 300 ms |
| `victory.wav` | C-E-G-C 四音琶音 | 485 ms |

做法是"快速起音（约 3ms）+ 指数衰减"包络的正弦/方波/噪声，拼成 16-bit 单声道 44.1kHz PCM 再加 WAV 头
（`Core/Audio/WavSynth.cs`，纯数学，所以能进 Core 单测）。

**文件已存在就不覆盖** —— 想换成自己的音效，把同名 wav 丢进 `%LOCALAPPDATA%\MineSweeper2\sounds\` 即可，不需要改代码。
切换音效开关与音量在设置面板，存在 `%LOCALAPPDATA%\MineSweeper2\settings.json` 的 `audioEnabled` / `audioVolume`。

每个音效各持一个 `MediaPlayer` 实例（一个实例只能承载一个源，共用一个会让"踩雷"打断"翻开"）。
音频子系统初始化失败只记 `app.log`，绝不影响游戏本身。

### 5.3 为什么单独一个 settings.json

文档 §6.1 的两张表是"跨局进度"和"记录"，都已带 `schemaVersion` 且经过验证。
音效/主题属于"玩家设置"，单独一个 `settings.json` 既不用动那两个存档的版本，
也让"可手改的设置"集中在一处（与 `config.json` 同一种交互方式）。它同样具备原子写盘、
损坏备份重建、版本过新拒读不覆盖这三条保护。

---


## 6. 无障碍、触屏、关卡制、关于页面与文档里的可选项

文档把这些写在 §1.4 / §1.5 / §2.6 / §3.2 / §4.1 / §5.3 / §5.4 / §5.6，属于"明确要求 / 可选 / 备选"，
本轮全部落地；最后一小节（§6.10）是文档里<b>没有</b>、额外加的关卡模式。

### 6.1 无障碍：自绘棋盘的 UI Automation（§1.4 / §3.2）

方案 A（单一 Win2D 路径）的既定代价是失去控件自带的无障碍语义树，所以必须自己提供 peer：

- 棋盘是一个 `Pane`，Name 里带盘面摘要，例如
  `扫雷棋盘 9行×9列，剩余 10 雷` —— 这正是文档要求能读出的信息。
- 子节点是虚拟格（`BoardCellAutomationPeer`），每格的名称是完整状态，
  例如 `第3行 第5列，已翻开，数字 2`；**只为可见区域生成并限制在 400 格内** ——
  1000×1000 有 100 万格，全铺进无障碍树会把辅助技术卡死。

> **本工程刻意把键盘操作整个去掉了，棋盘只认鼠标**（这是项目所有者的明确要求）：
> 棋盘不是 `TabStop`、不接收任何按键、也没有焦点格，因此**不会再出现那个蓝色的焦点小方框**。
> 与之配套，无障碍这一层是**只读**的：`BoardCellAutomationPeer` 只提供"念什么"，**不提供 Invoke
> 与聚焦模式**，所以辅助技术也读不到一条"不用鼠标就能操作"的路径。
> 棋盘的操作说明（`BuildBoardAutomationHelpText`）因此只描述鼠标操作。

### 6.2 触屏（§1.5 / §5.3）

- 双指捏合缩放 + 拖动平移（`ManipulationMode = Scale | TranslateX | TranslateY`）。
- 长按物品栏图标进入瞄准态（`Holding` 的 Started，即系统约 500 ms 的长按）。
- 触摸松手不再一律当点击：手势进行中、或按下到松开移动超过 8 DIP 都不触发翻开 ——
  手指很难完全不动，没这条在触屏上会误翻一大片。

### 6.3 锁定盘面（§2.6 的"或提供锁定盘面模式"）

普通模式"首点后才布雷"，所以**同 seed 换个首点就是另一张盘**。锁定盘面先按 seed 在全盘均匀布雷、
再从中心向外取一个安全首点（找不到就把安全区内的雷搬出去，**雷数不变**），于是**同 seed 必定同盘**。

- 设置面板有开关；分享串多一节 `:1` 表示锁定，解析兼容旧的 5 节格式。
- **开局不替玩家翻开首点**：雷位在创建时就按 seed 定好，但棋盘保持整盘未翻开、计时为 0，
  等玩家自己点第一下。因为锁定盘面的雷位不能像普通模式那样"围着首点布雷"，
  所以**首次翻开会被钉到开局就算好的那格安全点上**——玩家点哪一格都行，结果都是从安全点展开，
  既不会踩雷，盘面也仍然与 seed 一一对应（`RevealResult.Index` 返回真正翻开的那一格）。
  第二次点击起就完全按玩家点的格子算。
- 实测（`SAOLEI_LEVELS=check`）：开局 `状态=NotStarted、已翻开=0`；点第一格后安全点展开、计时开始。

### 6.4 远景雷密度热力图（§1.5 的可选叠加）

按**块**（约 8 DIP 见方）统计平均邻雷数再着色，而不是逐格：远景本就看不清单格，
逐格着色还会把"同色行程合并"彻底打碎（实测逐格 130 ms/帧 vs 块状 16 ms/帧）。
密度直接用已经算好的 `Cell.AdjacentMines`，零额外计算。

**默认关闭** —— 它画的是未翻开区域真实的雷密度，开着会破坏 §0 "100% 还原原版"的公平性，
只作为辅助/演示视图存在。

### 6.5 透视迷雾（§5.4 的备选）

文档把"5 秒后恢复迷雾"列为备选并注明"两人团队不建议首版做"，所以做成配置项而不是写死：
`config.json` 的 `item.magnifier.fadeSeconds`，**默认 0 = 永久保留**（守住 §5.4 硬性约束 3）。
配成大于 0 时 UI 逐帧调 `ExpireItemReveals()`，到期把 `ItemRevealed` 位清掉 ——
渲染器不需要任何改动就画回"未翻开"，而且"该区域已透视"的拒绝判定也随之失效。

### 6.6 放大镜信息面板（§5.6 的备选）

瞄准或透视时右侧面板列出区域坐标与每格内容（`MagnifierReveal.ToPanelText`），用于无障碍与截图分享。
文本格式化放在 Core，所以能单测。

### 6.7 其它被文档点名的小项

| 文档 | 内容 | 落地 |
|---|---|---|
| §4.1 | "问号：黄色问号" | 经典主题默认改成黄色 `#E0A800`，并有测试防止改回蓝色 |
| §1.5 | 双击棋盘空白处 → 适应窗口 | 双击非格子区域即复位视图 |
| §3.2 | 数字键 1~3 对应物品栏三格 | 1 用放大镜；2/3 是预留槽，按下会明确提示"该槽位还没有道具" |
| §4.1 | 未翻开格做可缓存的位图切片 | 预渲染成格子位图，1 次 `DrawImage` 取代 1 次填充 + 4 条描边 |

### 6.8 程序图标与通知图标

图标是同一份资源，set 在两处（还有一处是系统管的，见下面"已知限制"）：

| 位置 | 做法 | 状态 |
|---|---|---|
| exe 内嵌（资源管理器 / 任务栏 / Alt+Tab） | `csproj` 的 `<ApplicationIcon>Assets\MineSweeper2.0.ico</ApplicationIcon>` | ✅ 实测 |
| 窗口标题栏与任务栏 | `AppWindow.SetIcon(AppPaths.AppIconFile)`（exe 已内嵌，这里再显式设一次） | ✅ 实测 |
| Toast 通知**正文区**的大图标 | toast XML 的 `<image placement="appLogoOverride" hint-crop="none" src="file:///.../ToastIcon.png"/>` | ✅ 实测 |
| Toast 通知**表头**（应用名左边的）小图标 | 由系统按 AUMID 解析，unpackaged 下改不动 | ⚠️ 见下 |

两个要点：

- **unpackaged 应用不能用 `ms-appx`**，所以通知图标必须是磁盘上的真实文件，用 `file://` 绝对路径引用
  （`new Uri(path).AbsoluteUri` 自动处理中文与空格转义）。文件不存在时**不写这一段** ——
  宁可少一个图标，也不能让整条通知因为非法 `src` 发不出去。
- 原始图标是**单帧 256×252**，直接当 exe 图标会让 16×16 的任务栏图标发虚。所以重新打包成
  **9 帧标准 ICO**（16/20/24/32/40/48/64/128/256；小尺寸用 32bpp DIB + AND 掩码，256 用 PNG 帧），
  并把 252 的边补成 256 正方形（居中补 2px 透明边，原图四角本来就是透明的，视觉无变化）。

> 说明：16×16 看起来仍然偏软，这是这套 3D 细节图（带尖刺的雷 + 格子 + 旗子）在 16px 下的固有极限，
> 不是转换问题。要做到 16px 锐利，需要另画一版**简化图形**的小尺寸图标。

#### 已知限制：通知表头那个小图标，unpackaged 下改不了

通知表头（应用名 `MineSweeper2` 左边）的小图标**不是** toast 内容的一部分，
而是系统按 **AUMID 解析出的"应用身份图标"**，所以 `appLogoOverride` 影响不到它；
unpackaged 运行时它一直是系统那个通用的"应用"占位图。

以下办法**逐一实测过，全部无效**：

| 尝试 | 结果 |
|---|---|
| 把 AUMID 注册项的 `IconUri` 指向我们的 `.ico` | ❌ 无效 |
| 两个注册项（exe 路径键 / GUID 键）的 `IconUri` 都指向 96×96 PNG | ❌ 无效，且 `Register()` 每次还会把 GUID 键写回它自己生成的 16×16 |
| `Register()` 之后覆盖 WinAppSDK 生成的那张图标缓存（16×16 → 96×96） | ❌ 无效，表头并不读它 |
| 重建外壳图标缓存（`ie4uinit -show`）+ 重启资源管理器 | ❌ 无效 |
| 按微软 Win32 文档建开始菜单快捷方式并写入 `System.AppUserModel.ID` | ❌ 无效 |

按 [How to enable desktop toast notifications through an AppUserModelID](https://learn.microsoft.com/en-us/windows/win32/shell/enable-desktop-toast-with-appusermodelid)
的说法，桌面应用的通知身份应当来自带 AUMID 的开始菜单快捷方式；实测在本机对 unpackaged 应用不生效。
（上述尝试用的快捷方式与注册项**均已清理/还原**，代码里没有留下任何相关改动。）

**这个限制的正式解法是 MSIX 打包**：那时应用身份（名称 + 图标）由包清单的
`uap:VisualElements` / `Square44x44Logo` 提供，表头图标自然就是我们的图标。
目前按要求不打包，因此**记录为已知限制**，留到做 MSIX 时一并解决。

### 6.9 M2 验收标准：实测（这条原本从没被验证过）

文档 M2 的验收标准是"**1000×1000 棋盘缩放到全景不卡顿**"。为此加了 `RenderPerfProbe`：
在**真实 GPU 设备**上开离屏画布，调用**真正的** `BoardRenderer.Draw` 连画若干帧取平均，
并把每档渲染结果导出成 PNG（`%LOCALAPPDATA%\MineSweeper2\perf\`）供肉眼复核。

1000×1000 / 10 万雷，画布 1200×800（五次取样）：

| 缩放 | 可见格数 | 优化前 | 优化后 | 提升 |
|---|---|---|---|---|
| 0.02×（全景） | 1,000,000 | 332.34 ms（3 FPS） | **14.38 ms（70 FPS）** | 23× |
| 0.1× | 142,296 | 48.27 ms（21 FPS） | **2.11 ms（474 FPS）** | 23× |
| 0.5× | 5,766 | 13.87 ms（72 FPS） | **9.03 ms（111 FPS）** | 1.5× |
| 1× | 1,457 | 3.54 ms（283 FPS） | **1.85 ms（540 FPS）** | 1.9× |

**结论：验收达标。** 同时得到一个反直觉的发现：瓶颈是**绘制调用次数**——实测每格约 0.34 µs、
耗时几乎线性于可见格数，所以文档建议的"位图切片"（每格仍是一次调用）**不是主要解药**；
真正的解药是远景把**同色连续格合并成一条矩形**、再把结构相同的连续行合并成**竖直色带**，
100 万次调用塌缩成个位数～几十次。位图切片仍然做了，它对正常缩放有 1.5× 的收益。

顺带修掉一个视觉缺陷：格宽只有 0.52 DIP 时，逐格矩形的抗锯齿边缘会把大片同色区混出一条条横纹；
远景改成"纯色块 + 关闭抗锯齿"后，相邻矩形精确对齐像素，条纹消失（导出图从 51 KB 降到 14 KB）。

触发方式：设置面板的「性能自检」按钮，或启动前设环境变量 `SAOLEI_PERF=1`（结果写进 `%LOCALAPPDATA%\MineSweeper2\app.log`）。

### 6.10 关卡模式（新增玩法，开发文档里没有）

开发文档从头到尾没有关卡制（搜"关卡 / 闯关 / 章节 / 战役 / 难度曲线"全部无命中），
这是本工程额外加的一层玩法。

**设计一句话：关卡 = 一张固定的盘 + 一个目标。**

- **固定盘**：每关写死一个 seed，并<b>用锁定盘面模式创建</b>，所以同一关永远是同一张盘 ——
  这是关卡能重试、能刷星、能分享的前提（重试时盘面不变，失败才有意义）。
- **目标**：星级按**通关用时**评 —— ★ 通关 / ★★ ≤ 目标时间 / ★★★ ≤ 挑战时间。
  刻意<b>不做强制限时</b>：扫雷的失败条件本来就是踩雷，再加倒计时等于惩罚"想清楚再点"这种正确玩法。
- **逐关解锁**：第 1 关永远开放，其余需要上一关已通关。共 6 章 × 6 关 = 36 关。
- **变化性**：盘面尺寸、雷数、可用放大镜数、是否启用首点扩展安全区，逐关不同
（第三到第六章一律关掉扩展安全区；道具是全局共用的，所有关卡都能用）。

**关卡设计（要调难度就改这一张表）**：`Core/Levels/LevelTable.cs`

| 章 | 盘面 | 雷数 | 三星门槛 | 二星门槛 |
|---|---|---|---|---|
| 第 1 章 入门 | 9×9 | 10 → 20 | 50s → 90s | 90s → 160s |
| 第 2 章 进阶 | 16×16 | 30 → 55 | 140s → 240s | 240s → 420s |
| 第 3 章 高手 | 30×16 | 70 → 120 | 280s → 540s | 480s → 900s |
| 第 4 章 专家 | 30×16 | 130 → 180 | 600s → 800s | 900s → 1200s |
| 第 5 章 大师 | 30×18 | 150 → 225 | 750s → 1010s | 1120s → 1520s |
| 第 6 章 宗师 | 32×20 | 190 → 290 | 1040s → 1310s | 1560s → 1960s |

道具是**全局共用**的（v0.6 起）：初次三种各送 2 个。获得方式在 v1.4 改成**金币商店**（每扫出一颗雷 +1 金币，
放大镜 30 / 声呐 20 / 冻结 20；金币与买到的道具只在本局、本关有效）——
所以这张表不再有"每关给几个放大镜"这一列（`MagnifierCount` 已降级为纯设计数据）。

**关键取舍：关卡模式完全不影响原有玩法。**

| 项目 | 普通模式 | 关卡模式 |
|---|---|---|
| 存档 | `records.json` 最快记录 + `progress.json` 跨局进度 | **只写 `levelprogress.json`** |
| 道具 | 消耗账号库存 | 与普通模式**完全相同**：共用同一份 `progress.json`（v0.6 起） |
| 首点 | 点下去才布雷（同 seed 换首点就是另一张盘） | **开局就已布雷但整盘未翻开**，等玩家自己点第一下；首次点击落在开局算好的安全点上（不踩雷、盘面仍固定） |
| 模式 | **v0.6 起固定"经典 + 道具"**（设置里的模式下拉已删除） | 与普通模式完全相同 |
| 记录 | 可进最快记录表 | 不进记录表（成绩记在关卡进度里） |

这样 §0 那 10 条"还原原版"的东西与最快记录表一点没被污染，关卡制是一层干净的加法；
唯一例外是**道具与历史记录**：它们全局共用，在关卡里拿到/用掉/累计的会记进账号（v0.6 有意的统一）。

**进度与战绩**：`%LOCALAPPDATA%\MineSweeper2\levelprogress.json`，与其它存档一样具备原子写盘、损坏备份重建、
`schemaVersion` 过新拒读。每条记录存 `cleared` / `stars` / `bestSeconds` / `attempts`。

**界面**：

- 底部「关卡模式」按钮 → 关卡面板：按章分组，每关显示盘面、星数、最好用时、星级门槛与锁定状态，
  顶部汇总"已通关 x / 36 关 · 星数 y / 108"。面板上没有前后跳按钮，只有「返回游戏」。
- **◀ 上一关 / 下一关 ▶ 在游戏内**（关卡局才显示），不用退回面板就能连着往下打。
  **下一关要已解锁才可点** —— 第 1 关永远开放，其余需要上一关已通关，所以它平时是灰的，
  ToolTip 会写明是"通关本关后解锁"还是"已经是最后一关了"；第一关的「上一关」同样是灰的。
  点它**不会**弹出关卡面板，直接换关。
- 关卡列表的**列数跟着面板可用宽度走**（够宽 3 列，窄了 2 列），窗口拖窄时不会出现
  "一行只放得下两个、第三个掉到下一行"的怪样子。
- 关卡局会多一条**目标条**，实时显示"现在通关可拿几星"：
  `1-1 第一次点击 · 9×9 / 10 雷 · 现在通关可拿 ★★★（★★ ≤ 90s / ★★★ ≤ 50s）· 放大镜 ×1`。
- **通关会弹一张胜利结算小卡片**（主窗口内的模态层，不是独立窗口）：大字星级 + 本局用时 +
  最好成绩 +（破纪录时的）🏆 + 刚解锁的下一关，下面三个按钮：**重开这一关 / 下一关 / 返回游戏**。
  失败**不弹** —— Toast 已经说清楚了，而失败会反复发生，每次都拦一个弹窗会把"重试一下就好"的节奏打断。
- 通关/失败都发 Toast：通关报星数与最好成绩、并提示刚解锁的下一关；失败提示"同一关盘面固定，可重试"。
- 关卡局与普通局**共用同一套物品栏**（v0.6 起），不再有关卡专用库存；商店买到的数量也只在本局有效。
**验证方式**：`SAOLEI_LEVELS=check` 启动会跑一遍端到端自检 ——
构建面板 → 开始 1-1 → 造一次通关 → 校验星数/解锁/落盘 → **校验结算弹窗的可见性与文案** →
重开同一关校验"同一张盘" → 造一次失败（校验不弹结算）→ **点一次「下一关 ▶」进 1-2、再点「上一关 ◀」回到 1-1**。
结果写进 `%LOCALAPPDATA%\MineSweeper2\app.log`（`[LEVELCHECK]` 前缀）。

之所以用自检而不是手动玩：这台机器上合成输入送不进应用（前台锁定 / UIPI），
而自检走的是**和真人玩完全相同的代码路径**（Core 的状态机 → `HandleGameOver` → 结算 → 落盘 → 刷新面板），
只有"鼠标点下去"这一步不同。另外 `SAOLEI_LEVELS=ingame` 直接进第 1 关并保持游戏画面
（核对游戏内的前后跳按钮），`=panel` 停在关卡面板，`=play` 只进第一关，`=layout` 报出各行真实高度。

### 6.11 关于游戏 / 关于作者（新增）

底部两个按钮各开一个**独立小窗口**（不是主窗口里的覆盖层）：

- **关于游戏**：一句话简介、还原了什么 / 新加了什么、技术栈与规模、开源地址、许可，
  以及"本工程不含任何来自 Windows 7 扫雷的素材"这条版权红线（文档 §7.3）。
- **关于作者**：主开发 **Fastrof**、第二开发 **Shinichi**，各配一张圆形头像与分工说明；
  下方另有「贡献者」栏，第一位是 **LLJ99999**（在校大学生，负责 Minesweeper 2.0 官方网页的全新重新设计）；
  底部标注"两位都是计算机专业在读的大二学生"（文案在 `AboutInfo.AuthorsNote`）。

**窗口尺寸与样式对齐 Windows 自己的"关于 Windows"（winver）**：

| 项 | 做法 |
|---|---|
| 尺寸 | 实测 winver：**150% 缩放下外框 550×528 物理像素**（可视 536×521），即 **367×352 DIP**。`AppWindow.Resize` 按 DIP×DPI 换算，所以 100% 下 367×352 物理、150% 下 550×528 物理，观感与 winver 一致（旧版曾把 550×528 当 DIP 再乘缩放，导致 150% 下窗口大 1.5 倍，v1.4 已修） |
| 不可缩放 | `OverlappedPresenter.IsResizable = false` |
| 不可最大化 | `OverlappedPresenter.IsMaximizable = false` |
| 不可最小化 | `OverlappedPresenter.IsMinimizable = false`（winver 标题栏上也只有一个关闭按钮） |
| 跨缩放 | 尺寸按 DIP 给、再乘 `GetDpiForWindow` 得到物理像素，所以别的缩放比例下观感一致 |

> 这里踩过一个坑：一开始用 `XamlRoot.RasterizationScale` 取缩放比例，但首次 `Activated` 时它还是 1.0，
> 窗口只有该有的大小的 1/3。换成 `GetDpiForWindow` 之后尺寸才准。

**"不能拖大 / 不能最大化"是硬验证过的**（不是"调了 API 就信"）：直接读窗口样式位，
两个关于窗口都是 `WS_THICKFRAME=False`、`WS_MAXIMIZEBOX=False`、`WS_MINIMIZEBOX=False`，
而主窗口仍是三项全 True。

其它两个实现细节：

- **两个关于窗口的标题栏图标**也用工程自己那份多尺寸 ICO（`AppWindow.SetIcon`）。
  实测与主窗口、exe 内嵌图标**逐像素一致**（取窗口的 `WM_GETICON` 句柄转位图后比对）。
  注意用的是 `Assets\MineSweeper2.0.ico`（9 帧）而不是仓库根目录那份单帧原图——
  后者缩到 16×16 会发虚。
- **头像先缩小再随包分发**：原图 1448×1448（2.4 MB / 1.3 MB），页面上只按 60 DIP 显示，
  所以已缩到 **256×256**（163 KB / 103 KB）；贡献者 LLJ99999 的原图 1447×1449（791 KB）同样
  居中裁成正方形后缩到 **256×256**（约 80 KB）。原图不参与构建。
- **头像用文件路径加载，不走 ms-appx**：unpackaged 下 ms-appx 不可靠（做通知图标时踩过这个坑）；
  圆形头像用 `Border` + `CornerRadius` + `ImageBrush`（WinUI 的 `RectangleGeometry` 没有圆角属性）。

**仓库已公开**：`src\MineSweeper.App\AboutInfo.cs` 里的 `RepositoryUrl` 已填成
`https://github.com/Fastrof/Minesweeper2.0`（关于页面会直接显示这个链接；**留空时**才显示"即将公开"）。
版本号、两位作者名、许可文案也都在这一个文件里；页面正文在 `AboutText.cs`。

---

### 6.12 道具系统（v0.6 扩展：声呐扫描 / 时间冻结；v1.4 改为金币商店）

文档 §5 原本只有一个放大镜。v0.6 加了两个道具，v1.4 又改了获得方式与放大镜的形状：

- **三种道具**：放大镜（原有；v1.4 起透视 **2×2**，点中的格子是左上角）、声呐扫描（只报 3×3 里的雷数）、时间冻结（暂停计时 20 秒）。
  定义在 `Core/Items/`：`ItemIds`（稳定 Id 与显示名）、`MagnifierItem`、`SonarScanItem`、`TimeFreezeItem`。
- **声呐为什么不是"另一个放大镜"**：放大镜把区域标成"已获知"（信息进棋盘状态，UI 一直显示）；
  声呐**只返回一个数字**，不碰棋盘状态。所以它不翻开、不插旗、不影响胜负，也不受"已透视过就不能再用"的限制。
  两者共用 `AreaShape`（宽 × 高 + 锚点）：声呐是 3×3 居中，放大镜是 2×2 左上角，贴边统一按棋盘边界裁切。
- **冻结为什么不用"暂停计时器"**：`IClock` 是注入的抽象，最省事又最不容易漏改的做法是让
  `GameSession.ElapsedSeconds` 把冻结时长整体扣掉（`_frozenTotal` + 正在进行的那一段，单段上限 = 一次冻结时长）。
  这样计时显示、`LevelRules` 的星级判定全都自动跟着暂停。
- **获得方式（v1.4 金币商店）**：每扫出一颗雷 +1 金币（与"正确标记"同一事件，纯经典模式也照赚）；
  棋盘右上角「🛒 商店」随时可开，放大镜 30 / 声呐 20 / 冻结 20。商店的扣款与入账在
  `GameSession.TryBuyItem`（按 `ItemConfig.ShopPriceOf` 的价格扣、返回 `ShopPurchaseResult`），
  金币在 `CoinWallet` 上、挂在 `GameSession` 里 —— 新局 = 新钱包，所以"每局清零"是结构保证的。
  买到的数量进 `ItemInventory.SessionPurchases`（本局有效），消耗时**优先扣本局买的**。
  v1.4 同时**取消**了"每 20 颗雷三选一"：`pendingRewards` / `ClaimReward` 已移除，正确标记只累加统计。
- **道具栏**：`ItemInventory` 从"只有放大镜"改成三种道具的通用接口
  （`CountOf` / `IsFull` / `TryConsume` / `ClaimPurchase`），旧 API（`TryGrantMagnifier` / `IsMagnifierFull` /
  `MagnifierThreshold`）保留为兼容别名，避免动到既有调用点。
- **音效**：两个新道具复用现有音效（`Chord`），没有新增合成波形 —— 保持"零素材"。

### 6.13 历史记录（v0.6 新增）

`Core/History/PlayHistory.cs` + `Core/Persistence/HistoryStore.cs`，读写 `%LOCALAPPDATA%\MineSweeper2\history.json`。

- 从**第一次翻开格子**开始记（`BeginGame` 只认第一次的时间，但对局数每局都加）。
- 一局结束时把成果并进历史：用时、本局扫雷数（用"开局基线差值"算，避免与账号累计重复计）、
  翻开格数、是否胜利、是否通关关卡。`FlushHistoryForCurrentSession` 是唯一的累计入口，
  并用 `_historyRecordedForSession` 防止重复累计；换局时若上一局没走完结算也会先补记。
- 读写行为与其它存档一致：原子写盘、损坏备份重建、版本过新拒读且不覆盖原文件。
- **它是纯新增的独立存档**，删掉只影响历史本身。


### 6.14 设置面板逐项说明（v0.7）

设置面板（底部「设置 / 自定义」）里几个容易被问到的开关，一次说清：

| 设置项 | 是什么 | 什么情景用 | 代价 / 注意 |
|---|---|---|---|
| **窗口失焦时暂停计时** | 窗口一失去焦点就停表，切回来才继续（`MainWindow.Activated` → `GameSession.SetWindowFocused`） | 经常被打断、边玩边看别的程序、多显示器 | 只影响**用时**（关卡星级、最快记录）；**成绩表不区分**你开没开；和"时间冻结"道具是两回事（后者有数量限制、每次停 20 秒） |
| **锁定盘面（同种子必定同盘）** | **先**按 seed 全盘布雷、再挑一个安全首点；普通模式是"首点之后"才布雷 | 关卡模式（每关本质上都是锁定盘）、分享串复现、同一张盘反复练、复现问题 | v1.1 起：**只有点到雷**（或点到棋盘外）才把首点挪到**离点击位置最近的那个 0 格**（不是开局固定的那一格） —— 点到安全格就照玩家点的那一格开，"点了 A 却从 B 展开"只在点到雷时发生；开局**不**替玩家揭开第一格 |
| **远景雷密度热力图** | 按**块**（约 8 DIP）显示未翻开区域的**真实**雷密度（复用已算好的 `Cell.AdjacentMines`，零额外计算） | 教学/演示、超大棋盘先看结构、自己玩腻了想爽一把 | **会泄露信息**，破坏"100% 还原原版"的公平性，**默认关闭**；目前不会因此被挡在记录之外 |
| **首点扩展安全区** | 首点周围 5×5 一起翻开（更接近现代复刻的手感） | 想要宽松一点的开局 | 关掉更接近原版硬手感；**关卡第三到第六章一律关掉** |
| **主题 / 音效 / 音量** | 调色板 + 代码合成音效（零素材）；音量与开关立即生效 | — | 存在 `settings.json`，可手改 `themeOverrides`（例如只改 `coveredFill` 就能换未翻开格的颜色） |
| **模式下拉** | v0.6 起**已删除** | — | 原来要在"纯经典 / 经典 + 道具 / 自定义"里选，等于让玩家决定道具能不能用；现在一律"经典 + 道具"，道具默认可用 |

---


## 7. 文档条目 → 代码位置

| 文档 | 实现位置 | 状态 |
|---|---|---|
| §0 还原清单 1（左键翻开 / 右键三态） | `BoardView.RevealAt` / `BoardView.ToggleFlagAt` | ✅ |
| §0 第 2 条（首点安全 3×3） | `BoardGenerator.TryPlaceMines` | ✅ |
| §0 第 3 条（洪水填充，**迭代实现**） | `FloodFill.cs` | ✅ 含百万格回归测试 |
| §0 第 4 条（chord） | `ChordSolver` + `GameSession.Chord` | ✅ 含"旗插错会炸"原版行为 |
| §0 第 5 条（剩余雷数） | `GameSession.RemainingMines` | ✅ |
| §0 第 6 条（计时从第一次点击开始） | `GameSession.StartTimer`（第一次 `Reveal` 里触发） | ✅ |
| §0 第 7 条（三级难度） | `DifficultyPreset`（高级 30 宽×16 高） | ✅ |
| §0 第 8 条（数字八色） | `BoardTheme.NumberColor` | ✅ |
| §0 第 9/10 条（胜负、失败展示全盘雷） | `GameSession.CheckWin` / `BoardView.RepaintAfterAction` 的整屏重绘分支 | ✅ |
| §0.1 参数边界 | `GameLimits` + 明确报错通知 | ✅ |
| §2.1 数据结构 | `Cell` / `CellFlags` / `Board` | ✅ |
| §2.3 分帧展开 | `FloodFillOperation.Step` + `GameViewModel.Tick`（时间片） | ✅ |
| §2.5 O(1) 胜利判定 | `RevealedCount` 计数 | ✅ |
| §2.6 随机与种子 | seed 注入 + 分享串精确复现 | ✅ |
| §3.1 鼠标（含中键/左右同按 chord） | `BoardView.OnPointerPressed`（双键判定在最前） | ✅ |
| §3.2 键盘与无障碍 | **键盘已按项目所有者要求整个去掉**（棋盘只认鼠标，无焦点框）+ 自绘棋盘的 UI Automation peer（只读朗读） | ✅ 见 §6.1 |
| §4.2 计数器位数自适应 | `GameViewModel.FormatSeconds` / `D{digits}` | ✅ |
| §4.4 状态栏与物品栏 | `MainWindow.xaml` | ✅ |
| §4.5 大棋盘绘制策略 | `BoardRenderer` + `BoardViewport` 视口裁剪 + 区间/整屏两级重绘 | ✅ |
| §1.5 缩放平移 | `BoardView.ZoomAt/PanBy/FitToWindow/ZoomToCell` + 尺寸变化重新适应 | ✅ |
| §5 道具系统全部 | `Items/*` + 右键物品栏进入瞄准态 | ✅（7 项待拍板按文档建议取值） |
| §5.4 透视视觉反馈 | `BoardView` 高亮 1 秒淡出 + `BoardRenderer.DrawAreaOverlay` | ✅ |
| §4.1 自定义主题 | `Core/Themes/BoardPalette` + `App/Rendering/BoardTheme`（实例调色板） | ✅ 4 套内置 + 逐项颜色覆盖 |
| §1.4/§3.2 无障碍 | `BoardViewAutomationPeer` + `BoardCellAutomationPeer` | ✅ 读出"第3行第5列，已翻开，数字2" |
| §1.5 触屏 | `BoardView` 的 Manipulation（捏合/拖动）+ 物品栏 `Holding` 长按 | ✅ |
| §2.6 锁定盘面 | `BoardGenerator.TryPlaceMinesLocked` + `GameSession.TryCreateLocked` | ✅ 同 seed 必定同盘 |
| §1.5 雷密度热力图 | `BoardRenderer.DrawHeatOverlay`（按块统计） | ✅ 可选，默认关闭 |
| §5.4 透视迷雾（备选） | `ItemConfig.MagnifierRevealFadeSeconds` + `GameSession.ExpireItemReveals` | ✅ 默认永久保留 |
| §5.6 信息面板（备选） | `MagnifierReveal.ToPanelText` + 右侧面板 | ✅ |
| M2 性能验收 | `RenderPerfProbe`（真实设备 + 导出截图） | ✅ 100 万格 14.4 ms/帧 |
| 程序/通知图标 | `Assets\MineSweeper2.0.ico` + `ApplicationIcon` + `appLogoOverride` | ✅ exe / 标题栏 / 通知正文大图标均已实测；通知表头小图标受 unpackaged 限制，见 §6.8 |
| 音效 | `Core/Audio/WavSynth`（合成）+ `App/Services/SoundPlayer`（播放） | ✅ 零素材，可放同名 wav 替换 |
| §6 存档与记录 | `Persistence/*`（原子写盘）+ `AppPaths` | ✅ |
| §6.3 分享同一盘 | `GameSession.TryReplayShareCode` + 设置面板"按分享串复现盘面" | ✅ |
| （文档未涉及）关卡制 | `Core/Levels/*` + `MainWindow` 关卡面板/目标条/结算 | ✅ 新增玩法，见 §6.10 |
| （文档未涉及）关于页面 | `AboutInfo.cs` / `AboutText.cs` + `Views/About*Window` 两个固定尺寸小窗口 | ✅ 见 §6.11 |
| §7.1 打包 | 已产出 MSIX，商店版已上架（`artifacts/msix` 里有双身份清单、签名证书与商店素材；源码树本身仍是 `WindowsPackageType=None` 的 unpackaged 工程，打包走 §12.5 的手工流程） | ✅ 见 §12.5 |
| §8 测试计划 | `tests/MineSweeper.Core.Tests`（360 个测试，20 个测试文件 + `TestFactory.cs`） | ✅ 全绿 |

---


## 8. 文档里"待拍板"项的落地取值

| 编号 | 取值 | 说明 |
|---|---|---|
| A-1 渲染路径 | **方案 A：单一 Win2D 路径** | 经典三级难度只是一组预设参数（见 `MainWindow.StartPreset`） |
| A-2 首点扩展安全区 | **默认开启**，可在设置里关 | 见下方"实现决定 ③" |
| A-3 参数上限 | 4096×4096；雷数 ≤ N − 9 | `GameLimits`；非法参数报错并拒绝开局 |
| A-4 失焦暂停计时 | **默认不暂停**，设置项可开 | `GameSettings.PauseTimerOnFocusLoss` |
| A-5 道具 7 项 | 按文档建议全部落地，v1.4 后有调整 | 阈值 20（v1.4 起只用于统计展示）/ 初始赠送各 2 / 持有上限 9 / 放大镜 2×2（点中格为左上角，`ItemConfig` schema v2 可改）；商店价格 30 / 20 / 20 |
| A-6 撤销 | **首版未做**（文档建议） | 道具消耗点已留 `ItemUsed` 标记，将来接入 undo 有抓手 |

---


## 9. 测试

```bat
test.cmd          :: 360 个测试，约 1 秒
```

**360 个用例分布在 20 个测试文件里**（`tests/MineSweeper.Core.Tests/`）。本轮新增的 17 条用例分散在
`ItemTests` / `PersistenceTests` / `UpdateTests` / `AreaShapeAndCoinTests` / `HistoryTests` 里（文件数仍是 20）；
**项目里没有逐文件的精确数字，以 `test.cmd` 的输出为准**，下面按文件列覆盖范围：

- `AchievementTests.cs` — 成就目录守门人、判定边界、隐藏成就、进度只增不减、存档降级
- `LevelTests.cs` — 关卡制（**关卡表的守门人** + 星级 + 解锁 + 存档 + 会话内关卡 + 翻开全部安全格判胜 + 前后跳转与链表边界）
- `UpdateTests.cs` / `UpdatePreferencesTests.cs` / `UpdateDownloadTests.cs` — 版本比较与规范化、GitHub JSON 容错、更新频率与暂停、选包与 SHA256 校验
- `ThemeAndAudioTests.cs` — 主题调色板、设置存取、音效合成
- `ItemTests.cs` — 道具计数与发放、商店购买、金币与库存（§8 第 7、8 条）
- `UiThemeTests.cs` — 界面颜色主题、棋盘与背景的对比度
- `LocalizationTests.cs` — 中英语言表完整性与实时切换
- `BoardGenerationTests.cs` — 布雷与参数边界（§8 第 1、2 条）
- `AdvancedFeatureTests.cs` — 锁定盘面（含"开局不自动翻开 / 首次点击必定安全"）、透视迷雾、信息面板
- `HistoryTests.cs` — 历史记录（第一次扫雷起累计 + 删文件即重置 + 损坏/版本处理）
- `RegressionTests.cs` — 已修缺陷的回归防线
- `PersistenceTests.cs` — 存档与记录（§8 第 10 条）
- `AreaShapeAndCoinTests.cs` — 2×2 左上锚点区域形状、金币钱包与商店价格（v1.4）
- `WinConditionTests.cs` — 胜负判定（§8 第 6 条）+ 胜利自动插旗
- `ChordTests.cs` — chord 判定与"旗插错会炸"（§8 第 5 条）
- `FloodFillTests.cs` — 洪水填充（§8 第 3、4 条，含 1000×1000 全空盘不栈溢出）
- `LockedFirstClickTests.cs` — 锁定盘面首点：点安全格原地开、点到雷挪到最近的 0 格、雷位不因点哪而变
- `NotificationTests.cs` — 通知级别与"只入日志"维度

§8 十条规定用例的覆盖情况：

1. 布雷：雷数正确 + 首点格及 8 邻格无雷（角落/中央都测）
2. 参数非法：M > N − 9 拒绝开局且不布雷；尺寸越界、负雷数、棋盘放不下安全区都拦下
3. 洪水填充（小盘/单行）：手算盘面验证展开范围、不越雷列、行不环绕
4. 洪水填充（大盘）：**1000×1000 全空盘不栈溢出**并验证分帧推进
5. chord：旗数相符才展开；不符不展开；**旗插错位置踩雷失败**
6. 胜负：开完最后一个安全格立刻取胜；踩雷即失败；结束后禁止操作
7. 道具计数：同一雷反复插拔只计 1 次；插在安全格不计数
8. 道具与金币：初赠三种各 2 个且只送一次；持有上限 9；扫出一颗雷 +1 金币（与"正确标记"同一事件，
   纯经典模式也照样赚）；商店按 `config.json` 的价格扣款，买不起 / 满仓一分不扣；金币与"本局购买"每局清零。
   （v1.4 起取消了"每 20 颗雷三选一"，阈值只留给统计展示。）
9. 透视：2×2 且点中的格子是**左上角**（贴边按棋盘边界裁切，最多 4 格、边角更少）；
   **不算翻开、不自动插旗、不触发胜利**；首点前禁止；宽高跟随配置
10. 存档：保存读取一致；损坏 JSON 降级重建并留备份；低版本迁移；高版本拒读且不覆盖原文件；记录分表

回归测试额外锁住：

- 改动区间**覆盖所有新翻开的格**（洪水填充/单格/chord），踩雷时区间为空（UI 据此整屏重绘），区间取走即清空
- 大盘（120×80 / 300 雷）邻雷数与朴素两重循环结果完全一致
- 首次赠送只在第一次真正开局时发放一次（v0.6：三种道具各 2 个）
- `records.json` / `progress.json` 原子写盘后不留 `.tmp`，覆盖写与首次写都能正确读回
- 分享串可精确复现同一盘面（雷位/首点/安全区设置全等），非法分享串被拒绝
- 按每帧块大小分帧推进也能完整展开（不会卡死在中途）

主题与音效（`ThemeAndAudioTests.cs`）：

- 颜色三种写法（`#RGB`/`#RRGGBB`/`#AARRGGBB`）解析正确，非法颜色被拒绝，往返一致
- 4 套内置主题 Id 唯一、颜色全部合法、数字八色齐全；坏颜色在 `Normalize` 后回落经典配色
- **调色板认得 App 用到的全部 19 个颜色键**（键名写错会静默回落，这个用例专门防这个）
- 逐项覆盖只改指定颜色，非法值与未知键被丢掉，未知主题回落经典
- `settings.json` 往返一致、原子写盘不留 `.tmp`、损坏降级重建并留备份、高版本拒读且不覆盖
- 设置规范化：音量夹到 0..1、未知主题回落、非法覆盖项剔除
- 5 个音效都生成**合法且非静音**的 WAV（RIFF/WAVE/fmt/data 标记、长度字段自洽、16-bit 单声道 44.1kHz）
- 5 个音效波形互不相同
- 经典主题的问号是黄色（防回退到蓝色）

锁定盘面与可选项（`AdvancedFeatureTests.cs`）：

- 锁定盘面**同种子必定同盘**（雷位与首点全等）、不同种子得到不同盘
- 锁定盘面首点安全：自己与 8 邻格无雷，且雷数正确（9×9/16×16/30×16 都测）
- 雷极密（5×5 / 16 雷，M 已达上限）时也能把安全区内的雷搬出去，雷数不变
- 锁定盘面的分享串带 locked 标志且能精确复现；**旧 5 节格式仍可解析**（视为非锁定）
- 透视信息默认永久保留；配成 2 秒后到期真的清除透视标记并报出重绘范围
- 迷雾清掉之后"该区域已透视"的拒绝判定随之失效，可以再次透视同一区域
- 信息面板文本包含 1 起的行列坐标与每格内容（雷/空/数字），角落只覆盖 4 格

关卡制（`LevelTests.cs`）：

- 关卡表结构：6 章 × 6 关 = 36 关、编号连续、Id 唯一且与章内序号自洽
- **每一关的参数都合法**（过 `GameLimits` 校验，否则那一关根本开不了局）
- 星级门槛单调且为正（挑战时间必须严于目标时间，否则三星没有意义）
- 每一关的种子互不相同（否则两关是同一张盘）
- **每一关都能创建、且同一关永远是同一张盘**（雷位与首点全等）；开局已翻开首点、
  首点及 8 邻格无雷，且**没有"一开局就赢"的白送关卡**
- 关卡与普通模式共用同一套道具（v0.6）：在关卡里用掉一个，账号库存直接减少
- 星级评定边界（正好等于挑战时间 / 目标时间各测一次）、星数只升不降、最好成绩只取更快
- 逐关解锁：第 1 关默认开放；通关后解锁下一关并报告"刚解锁的那一关"；失败不解锁、只累加尝试次数
- 进度规范化：重复项合并、非法值夹住、未通关清掉星数、未知关卡 Id 保留
- 存档往返一致 + 不留 `.tmp` + **不把计算属性（ClearedCount/TotalStars）写进 JSON**
- 损坏降级重建并留备份；版本过新拒读且原文件一个字节都不动

---


## 10. 实现决定与已知待办

**实现决定（文档没写死、由实现补上的地方）**

1. **计时起点**：从第一次"翻开"开始计时（右键插旗不计时），与 §0 第 6 条"第一次点击"一致。
2. **棋盘尺寸下限**：文档只规定上限，下限交给"首点 3×3 安全区"兜底（即 `N ≥ 9 + 雷数`），所以 `1×N` 单行棋盘也合法。
3. **首点扩展安全区与 §0 第 2 条的关系**：因为 §0 强制"点击格 + 8 邻格"无雷，首点格邻雷数天然为 0，
   "首点必开一片空区"已经成立；`FirstClickOpening` 再把安全区扩到 5×5（雷太密时自动退回 3×3）。
4. **存档位置**：§7.2 指的是 MSIX 场景。现在无论打包与否都写在 **`%LOCALAPPDATA%\MineSweeper2\`**
   （打包版走包的 `LocalFolder`），v0.6 起就是这样；exe 旁边的 `data\` 只是历史遗留与旧数据搬迁来源。
5. **瞄准态光标**：用"把目标区域画成高亮框"替代"替换鼠标指针"（`UIElement.ProtectedCursor` 只能由派生类设置）。
6. **重绘范围用"格索引区间"而不是"格集合"**：区间是真实改动集合的超集（洪水填充的索引区间天然连续），
   只要两个 `int`、零分配；`Consume` 语义＝报告一次就清空，防止陈旧区间被重复消费。
7. **分享串能精确复现的关键**：`TryReplayShareCode` 会把 `FirstClickOpening` 强制设为分享串里的
   `UsedExtendedSafeZone`，所以本机设置与分享方不同也能生成同一张盘。
8. **首次赠送（v0.6 起改为"三种各 2 个、统一发放"，见 §0.5.3）**：v0.6 及以前只在启用道具的模式发放 —— 纯经典模式是"无道具、无物品栏"（§5.5 ⑤），
   在那里把赠送标记掉会让玩家切模式后拿不到本该属于他的那一个。
9. **通知级别不用 `scenario="urgent"`**：理由见 §4。
10. **主题就是调色板**：既然棋盘全是矢量自绘，主题不需要图片素材；颜色用十六进制字符串放在
    `settings.json` 里（`themeId` + `themeOverrides`），可手改到单个颜色。数据模型在 Core（可单测），
    App 只做 `Windows.UI.Color` 映射。
11. **音效由代码合成**：理由与"自绘棋盘"同源（§1.2.1 ④ / §7.3 版权红线），零素材、零体积。
    合成结果落到 `%LOCALAPPDATA%\MineSweeper2\sounds\`，**已存在的不覆盖**，所以想换成自己的音效只需放同名文件。
12. **设置单独一个 `settings.json`**：不动 progress/records 两个已验证的存档版本，理由见 §5.3。
13. **设置控件的变更必须等构造完成**：XAML 里 `IsOn="True"` / `Value="70"` 会在
    `InitializeComponent()` 解析期间就触发 `Toggled` / `ValueChanged`，而那时播放器字段还没赋值——
    实测会直接把启动打崩。所以用一个 `_settingsReady` 闸门把它们挡在构造完成之前。
14. **远景不逐格绘制**：合并同色行程 + 合并结构相同的连续行 + 关闭抗锯齿。
    这是被实测逼出来的（瓶颈是绘制调用次数而不是像素），数据见 §6.9。
15. **无障碍节点只为可见区域生成、上限 400 格**：1000×1000 有 100 万格，
    把每一格都塞进无障碍树会让辅助技术直接卡死；虚拟格用 `FrameworkElementAutomationPeer`
    承载（Owner 传棋盘），省掉 `AutomationPeer` 几十个抽象成员的样板代码。
16. **触摸的"点击 vs 拖拽"双重判定**：手势进行中、或按下到松开位移超过 8 DIP 都不算点击——
    手指很难完全不动，只靠 PointerReleased 在触屏上会误翻一片。
17. **热力图按块统计而不是逐格**：见 §6.4，逐格既慢（130 ms/帧）又把行程合并打碎。
18. **关卡 = 固定盘 + 星级，不做强制限时**：每关写死 seed 并用锁定盘面创建，所以同一关永远是同一张盘；
    星级只看通关用时。不给倒计时是因为扫雷的失败条件本来就是踩雷，
    加限时反而惩罚"想清楚再点"这种正确玩法（详见 §6.10）。
19. **（v0.6 已改：见 §0.4.6 与决定 33）关卡模式与账号完全隔离**：关卡用独立的临时物品栏（数量由关卡给），成绩写进独立的
    `levelprogress.json`。这样"还原原版"的记录表与道具进度不会被新玩法污染，
    也避免了"刷关卡刷道具"这种数值漏洞。
20. **关卡数值集中在 `LevelTable` 一张表**：盘面、雷数、固定种子、星级门槛、道具数、安全区开关
    全是这一张表里的常量，调难度不需要碰任何逻辑代码。
21. **关卡自检用环境变量而不是合成输入**：`SAOLEI_LEVELS=check` 直接调 Core 的 `Reveal` 把一局打通/打输，
    其余全走正式路径。这台机器上合成输入送不进应用（前台锁定 / UIPI），
    这是唯一能端到端验证"通关 → 评星 → 解锁 → 落盘"的办法（见 §6.10）。

22. **只弹关键通知**：通知模型加了 `ShowToast` 维度。过程性提示（换主题、开关设置、新局开始、
    关卡开始、瞄准态、取消、视图复位、复制成功、点了翻不开的格子）一律改为**只写日志不弹窗**，
    并且不占用限流额度；真正弹窗的只剩"一局的结果 + 出错的 + 会让人以为程序坏了的拒绝"。
    实测：普通启动 0 条弹窗、换主题/改设置/新开一局 0 条弹窗（见 §4.1）。

23. **鼠标左键"按下即生效"、触屏才"松手判定"**：两者需求相反——鼠标要的是"点哪开哪"的即时手感，
    触屏要的是"手指抖动不算点击"。所以按 `e.Pointer.PointerDeviceType` 分流，
    并且**判定格一律用按下的坐标**（用松手坐标会随手指/鼠标移动落到隔壁格）。
24. **窗口跟着棋盘走**：开局时按"棋盘格宽 26 DIP + 界面装饰(宽 150 / 高 300)"算目标尺寸，
    再夹到屏幕可用区的 94% 以内。宽度有下限 700——不是给棋盘的，是给状态栏和两行按钮的；
    正因为窗口会变窄，底部按钮从一行改成了两行，状态文字也改成可伸缩列 + 省略号。

25. **胜利时自动给剩下的雷插旗**：扫雷的胜利条件不要求标出雷，但玩家看到"还有没翻开的格子"
    就会以为判胜是错的。原版赢了也会把雷标出来，所以这既是修 bug 也是还原原版表现。

26. **键盘操作整个移除、棋盘只认鼠标**：这是项目所有者的明确要求，**故意偏离开发文档 §3.2**。
    连带的取舍：`focusRing` 调色板键被删（没人再画焦点框）、"空格+左键平移"被删（没有触发者了）、
    无障碍 peer 降级为只读。滚轮仍支持 `Ctrl+滚轮` 缩放 / `Shift+滚轮` 横向，因为那属于鼠标手势。
27. **"重开这一局"的语义**：小黄脸 = 用**当前这一局真实生效的参数**重开（关卡局重开同一关；
    普通局沿用盘面尺寸/雷数/模式/锁定开关，种子取种子框）。参数从 `GameSession` 取而不是从界面控件取，
    这样"开局之后又改了模式或锁定开关"不会污染重开结果。以前这里写死 `Beginner`，
    于是玩 40 雷失败后点笑脸会掉回 10 雷。
    **v1.1 补充（修玩家报的 bug）**：失败弹窗上原来只有一个按钮，写着「重新开始这一盘」，
    实际走的是上面这条路径 —— 只沿用参数、种子留空就是**新随机盘**，所以它换了盘，
    与按钮文案自相矛盾（当时摘要里甚至同时写着"重新开始会换一张新盘"）。
    现在把两种意图**分开成两个按钮**，各自名实相符：
    - **「重打这一盘」= 同一张盘**（走 `CaptureSnapshot` + `CreateReplay` 精确复现雷位与首点，
      与分享串复现同一条路径），并且标记为**练习局**：玩家已经看过这局的雷位，
      成绩没有可比性，所以 `SubmitRecordIfNeeded` 直接跳过最快记录表并说明一句。
    - **「换一张新盘」= 原来那条路径**（沿用参数、新随机盘、成绩照常计）。
    - 判定"是不是练习局"用"当前会话是否就是那个练习会话"（引用比较），不额外维护布尔开关：
      任何开局路径一换会话标志就自动失效，不必去每个开局方法里补复位。
    - 小黄脸保持原语义（同参数、新盘）不变：它没被报为 bug，且"重开一局"本来就理解为换一局。
28. **通知共用一个槽位 + 启动清空积压**：理由见 §4.3。核心是"Toast 排队"这个系统行为——
    不主动用替换语义的话，"操作"和"响铃"之间必然有延迟。这是玩家反馈的体验问题，
    不是代码 bug（代码一直"发送成功"），所以排查时要看**操作中心里的条数**，不能只看 `Show()` 有没有抛。



29. **两个新道具都"不改变棋盘状态"**：声呐只返回一个数字、冻结只影响用时。这样它们不会与
    "已获知信息不该回收"（§5.4 约束 3）、胜负判定、记录表这些既有规则打架 —— 加道具只加"怎么看信息"，
    不改"棋盘怎么变"，回归面小得多。
30. **奖励方式（v0.6 `GameSettings.ChoiceRewards`，v1.4 已整体移除）**：v0.6 起两种模式都是"每 20 颗雷三选一"；
    v1.4 改成金币商店后，`ChoiceRewards` / `pendingRewards` / `ClaimReward` 一并删除，
    正确标记（扫出一颗雷）只做三件事：+1 金币、累计统计、喂成就。阈值 20 只剩展示用途。
31. **历史记录是独立存档**：不塞进 progress.json，理由与 §5.3 给 settings.json 的一样 ——
    新玩法不该动已经验证过的存档版本；任何一个存档坏了/删了都只影响它自己。
32. **"删 `%LOCALAPPDATA%\MineSweeper2` 即全部重置"是一条约定，不是巧合**：所有写入都集中在 `AppPaths`
    （`File.AppendAllText` 只用于 app.log，其余走 `AtomicFile`），并审计过没有注册表 / `%LOCALAPPDATA%` 写入。
    新增存档时请沿用这个约定。

33. **道具不再分"普通库存 / 关卡库存"**：v0.6 的关卡库存是临时 `PlayerProgress`，好处是关卡成绩不污染账号，
    但代价是"关卡里拿到的道具出关就没、账号里的道具进关用不了"，玩家很难建立心智模型。
    v0.6 统一成一套（账号库存），代价是关卡里插的旗也计入账号统计 —— 这是有意接受的取舍。
34. **设置里的"模式"下拉删掉**：它把"能不能用道具"变成玩家的前置选择，而需求要的是"道具默认就能用"。
    Core 保留 `GameMode.ClassicPure` 这条能力（有单测），只是界面不再暴露 —— 将来要做"纯净榜"可以直接复用。
35. **初赠三种各 2 个 + 持有上限 9**：kicker 是"让玩家一定试到每一种道具"。2 个 = 够试一次还有一次备用。
    v1.4 把上限从 3 提到 9：金币每局清零、买到的道具也随本局清零，账号里的存量是长期累积的，
    上限还是 3 的话老玩家早就满了、商店对他们等于不存在。
**待办**

- ~~MSIX 打包（§7.1 第二步）~~ —— **已完成**：已产出 1.0 / 1.3 的 MSIX 并上架 Microsoft Store，
  产物、双身份清单、签名证书与商店素材都在 `artifacts/msix`（发版流程见 §12.5）
  - "通知表头小图标"随之解决：打包版由包清单的 `uap:VisualElements` 提供（§6.8）
- 撤销/回退（A-6，首版按文档建议不做）
- `themeOverrides` 支持逐项覆盖全部 19 个颜色键（含数字八色 `number1`..`number8`）；没有"整套调色板导入/导出"功能
- 远景热力图按设计会泄露未翻开区域的雷密度，只作辅助视图；若要用于正式对局需另行设计"只看已知信息"的版本
- 若 Win2D 后续出问题，按 §1.2.1 切 `SkiaSharp.Views.WinUI`（Plan B），Core 层无需改动
- 上传 GitHub 前记得补 `.gitignore`（`bin/`、`obj/`、`data/`、`*.7z` 都不该进仓库）——
  仓库里已附一份可直接用的
- `App/MainWindow.xaml.cs` 已经 1100 行左右，是当前最该拆的文件（输入/视口/瞄准态/手势/自检钩子都挤在一起）
---


## 11. 接手开发须知（写给下一位开发者）

### 11.1 现在的状态

| 项 | 状态 |
|---|---|
| 构建 | `build.cmd` → **0 警告 0 错误**（x64） |
| 测试 | `test.cmd` → **360 / 360 通过**，约 1 秒 |
| 运行 | unpackaged 双击 exe 即可（见 §2.1）；另有 MSIX 包并已上架 Microsoft Store（§12.5） |
| 开发文档进度 | A / B / D 类已全部落地；C 类（如把 `BoardView` 里的输入逻辑拆成 `Input/`）未做；MSIX 打包已完成 |
| 已知限制 | 通知表头小图标改不动（§6.8）；热力图会泄露雷密度所以默认关闭（§6.4） |

### 11.2 架构：改动前先确认"该动哪一层"

```
MineSweeper.Core   纯 .NET 类库，零 UI 依赖 → 所有规则都在这里，全部可单测
      ↑
MineSweeper.App    WinUI 3 + Win2D → 只负责显示与输入，不含游戏规则
```

**铁律：游戏规则不要写进 App 层。** 判断标准很简单——"这段逻辑能不能不启动 UI 就测"？
能测就该放 Core；Core 里连 `Windows.*` / `Microsoft.UI.*` 都不许出现。

| 想改什么 | 该动哪里 |
|---|---|
| 布雷 / 首点安全 / 锁定盘面 | `Core/BoardGenerator.cs` |
| 翻开的连锁反应 | `Core/FloodFill.cs`（**迭代实现，不要改成递归**） |
| chord / 胜负 / 计时 | `Core/GameSession.cs`、`Core/ChordSolver.cs` |
| 关卡数值（盘面 / 雷数 / 星级门槛） | `Core/Levels/LevelTable.cs`（**只改这一张表**） |
| 星级与解锁规则 | `Core/Levels/LevelRules.cs`（纯函数） |
| 存档读写 | `Core/Persistence/*`（都要走 `AtomicFile`） |
| 配色 | `Core/Themes/BoardPalette.cs`（数据）+ `App/Rendering/BoardTheme.cs`（映射） |
| 音效 | `Core/Audio/WavSynth.cs`（合成）+ `App/Services/SoundPlayer.cs`（播放） |
| 通知级别与"要不要弹窗" | `Core/Notifications/GameNotification.cs` |
| 绘制 | `App/Rendering/BoardRenderer.cs` |
| 输入 / 缩放 / 瞄准态 | `App/Views/BoardView.cs` |
| 状态栏 / 面板 / 主窗口 | `App/MainWindow.xaml(.cs)` |
| 关于窗口 | `App/Views/About*Window.*` + `App/AboutInfo.cs` / `App/AboutText.cs` |

### 11.3 常见任务怎么做

- **加一个主题**：在 `BoardPalette.BuiltIn` 里加一条记录（19 个颜色键）。
  主题下拉框会自动多一项，**不用写任何新代码**。键名列表由
  `ThemeAndAudioTests.调色板认得App用到的全部颜色键` 守着，写错会红。
- **加一个音效**：`SoundEffect` 枚举加一项 + `WavSynth` 里加一段波形，
  `SoundPlayer` 会在首次运行时合成到 `%LOCALAPPDATA%\MineSweeper2\sounds\`。
- **加一关**：在 `LevelTable.All` 里加一条。注意 ① 参数要过 `GameLimits.ValidateParameters`
  ② seed 不要与现有关卡重复 ③ 挑战时间必须小于目标时间 —— **这三条 `LevelTests` 全都会挡**
  （那批用例就是"关卡表的守门人"，故意写得很啰嗦）。
- **加一条通知**：在产生它的地方调 `GameNotification.Info/Success/Warning/Error`（会弹窗）
  或 `QuietInfo/QuietSuccess`（只写 `app.log`）。**先想清楚它是"结果/错误"还是"过程提示"**（§4.1）。
- **加一个存档字段**：结构体加属性 → `schemaVersion` +1 → 在 `Migrate` 里补迁移 →
  沿用 `AtomicFile` 写盘。已有 8 个存档按这个模式跑（settings / progress / records / levelprogress /
  history / achievements / updatestate / config）。

### 11.4 诊断开关（没法注入鼠标键盘时很有用）

| 环境变量 | 作用 |
|---|---|
| `SAOLEI_PERF=1` | 渲染性能自检（M2 验收标准），结果与对照图写进 `%LOCALAPPDATA%\MineSweeper2\` |
| `SAOLEI_LEVELS=check` | 关卡制端到端自检：建面板 → 开始 1-1 → 造通关 → 校验解锁与落盘 + **结算弹窗文案** → 重开验同盘 → 造失败 → **点一次「下一关」「上一关」** |
| `SAOLEI_LEVELS=panel` / `=play` / `=ingame` / `=layout` | 停在关卡面板 / 只进第 1 关 / 进第 1 关并保持游戏画面 / **逐档加高窗口报出根 Grid 每行真实高度**（查布局问题用这个） |
| `SAOLEI_NOTIFY=1` | 验证"精简通知"：换主题 + 改设置 + 新开一局后报告新增弹窗条数 |
| `SAOLEI_ABOUT=game` / `=authors` | 只打开对应的关于窗口（便于截图核对） |
| `SAOLEI_INPUT=1` | **左右键每次点击**都往 `app.log` 记一行 `[INPUT]`：算出来的格索引 + 当时的视口(cell / offset / 控件尺寸) + **该格状态**，并在动作后追加一行"本次翻开结果：N → M 格（Δ=…）"。**专治"点了没反应 / 点开的是别处"**：只要有这一行，就说明输入到达了棋盘；`index=-1` 表示点击落到了棋盘控件里但不在格子上（棋盘外围的空白区），`index>=0` 就是那一格。**没有这一行才叫"点击丢了"** |
| `SAOLEI_LEVELS=clicksweep` | **单击 / 双击行为扫描**：开一局初级，先左击开一片（随后静置 3 秒确认棋盘**不会自己继续展开**），再逐格走一遍"双击已翻开格"与"单击已翻开格"，逐条记 `[CLICKSWEEP]` 日志。用来守两条规矩：① 单击已翻开的数字格必须什么都不做；② 双击已翻开的**空白格（邻雷=0）**必须一格都不多开。判据：出现 `✗ BUG` 行、或本局中途变成 `Lost` |
| `SAOLEI_LEVELS=secret` | 摇一个隐藏关并直接进去（便于截图核对隐藏关盘面）；隐藏关的实现细节只在代码与 `HANDOFF.md` 里 |
| `SAOLEI_UI=sonar` | **声呐结果弹窗自检**：开一局"经典 + 道具"，进真实瞄准态 → 在棋盘中心放下去，然后报告 `SonarOverlay` 的可见性、标题、区域文案与剩余数量，并模拟点「知道了」确认收起（`[UICHECK]` 行） |
| `SAOLEI_UI=defeat` | **踩雷弹窗自检**：手动开一格后踩一颗雷，走真实状态变化链路，报告 `DefeatOverlay` 可见性与摘要，再模拟点「重新开始这一盘」确认新局是 `NotStarted`、已翻开 0 格 |
| `SAOLEI_UI=difficulty` | **切换难度 = 启动铃声**自检：依次点初级/中级/高级三个按钮的处理函数，逐次报盘面尺寸与 `[音效] 播放启动铃声` 日志 |
| `SAOLEI_UI=exit` | **退出按钮自检**：踩雷弹出失败窗口后调用「退出游戏」。判据以**进程是否消失**为准（`Close()` 是异步的，日志里那行一定会先写出来），随后应能看到"窗口关闭，进度与设置已落盘" |
| `SAOLEI_UI=play` | 把游戏摆成"**可直接手动试声呐**"的状态，然后停在这里不自动跑完 |
| `SAOLEI_UI=magnifier` | **放大镜形状自检（v1.4）**：开 16×16/40 的带道具局，进真实瞄准态 → 真的点在 (5,5)，日志报配置（宽/高/锚点，期望 `2/2/(0,0)`）与实际被透视的 4 格（应为 (5,5)(6,5)(5,6)(6,6)） |
| `SAOLEI_UI=shop` | **商店机制自检（v1.4）**：开 16×16/40 的带道具局 → 插旗赚金币 → 走真实按钮打开商店 → 逐个购买（买不起的、买得起的都试）→ 关商店；覆盖"金币来源 / 三种价格 / 买完扣钱 / 退出商店 / 每局清零"（`[SHOPCHECK]` 行） |
| `SAOLEI_NEWGAME=<秒>` | 延迟 N 秒后模拟"点一次笑脸重开"（走的就是按钮那条 `RestartCurrentGame()`），记录重开前后的**盘面参数**与**窗口状态**（`[NEWGAME]` 行） |
| `SAOLEI_TOASTBURST=1` | 间隔 400ms 连发两条通知（Warning + Error），然后查操作中心里本作还剩几条。**验证"新通知替换旧的、不排队"**：应当只剩 1 条且是后发的那条 |
| `SAOLEI_ITEMS=1` | 开一局"经典 + 道具"，每种道具各发一个并各用一次（便于截图与日志核对）。注意：默认开局是纯经典模式，**道具在该模式下本来就会被正确拒绝**；v1.4 起"三选一"奖励已移除，自检里只改 `progress.json` 的旧 `pendingRewards` 字段做兼容性检查 |
| `SAOLEI_HISTORY=1` | 塞一点历史数据并打开历史记录弹窗，便于核对累计与时长格式化 |
| `SAOLEI_FLAG20=1` | 开一局 30×16/99 的普通局，连插 20 面**正确**的旗并走一遍棋盘状态变化链路；
用来验证"每扫出一颗雷 +1 金币"（日志会打印金币数与右上角显示，期望 20） |
| `SAOLEI_LANG=en` | 强制界面语言为英文（`zh-Hans` / `en`），用于截图核对英文界面，**不改存档** |
| `SAOLEI_UITHEME=light` / `=dark` / `=system` | 强制界面颜色主题，用于截图核对，**不改存档** |
| `SAOLEI_PRESET=beginner` / `=intermediate` / `=expert` |' 启动时直接开指定难度；也支持自定义写法 `SAOLEI_PRESET=30x20/120`。配合 `SAOLEI_NEWGAME` 就能验证"重开会沿用当前难度" |

结果都写进 `%LOCALAPPDATA%\MineSweeper2\app.log`，前缀分别是 `[LEVELCHECK]` / `[NOTIFYCHECK]` / `[ABOUTCHECK]` / `[ABOUT]` / `[SHOPCHECK]` / `[MAGCHECK]` / `[UICHECK]`。

> 为什么会有这些钩子：开发机上**合成鼠标/键盘输入送不进应用**（前台锁定 / UIPI），
> 所以端到端验证只能靠它。它们走的是和真人操作**完全一样**的代码路径，只有"点下去"这一步不同。
> **接手后如果有正常的交互环境，这些开关留着无妨，删掉也可以。**

### 11.5 代码约定（照着写就不会跑偏）

1. **注释讲"为什么"，不讲"是什么"**。每条非平凡的决定都写清"为什么不是另一种做法"，
   尤其是踩过的坑（例如"`XamlRoot.RasterizationScale` 在首次 Activated 时还是 1.0，
   窗口会小 1/3"）。
2. **Core 零 UI 依赖**，App 层不写规则。
3. **存档一律原子写盘**（`AtomicFile`）；损坏要留备份、版本过新要拒读且不覆盖原文件。
4. **平台可能失败的地方都要兜住**：Toast / 音效 / 图标 / 存档，失败只写日志，
   **绝不影响游戏本身**（这是本工程最反复强调的一条）。
5. **新功能先想"能不能单测"**：能就先写 Core + 测试，再写 UI。
6. 玩家看到的一切都是中文；注释也是中文。

### 11.6 已知限制与待办

见 [§10](#10-实现决定与已知待办) 末尾的"待办"。另外有两条是**刻意保留**的：

- **通知表头的小图标**在 unpackaged 下改不动（§6.8 列了试过的 5 种办法，全部无效），
  正式解法是 MSIX 打包 —— 打包版已由包清单的 `uap:VisualElements` 提供。
- **远景热力图**会泄露未翻开区域的雷密度，所以默认关闭、只作辅助视图（§6.4）。

---


## 12. 发版清单（GitHub + Microsoft Store）

仓库地址：**https://github.com/Fastrof/Minesweeper2.0**（`AboutInfo.RepositoryUrl` 就是它）。

### 12.1 该提交的

| 范围 | 内容 |
|---|---|
| 根目录 | `MineSweeper2.sln`、`Directory.Build.props`、`Directory.Packages.props`、`build.cmd`/`run.cmd`/`test.cmd`、`.gitignore`、`README.md`、`MineSweeper2.0.ico`（图标原图，不参与构建） |
| `src\MineSweeper.Core\` | 整个目录：核心逻辑层（零 UI 依赖）+ `Audio/` `History/` `Items/` `Levels/` `Notifications/` `Persistence/` `Settings/` `Themes/` |
| `src\MineSweeper.App\` | 整个目录：WinUI 3 外壳 + `Rendering/` `Services/` `ViewModels/` `Views/` + `Assets/`（**4 个运行时资源必须带**：`MineSweeper2.0.ico`、`ToastIcon.png`、`Authors\Fastrof.png`、`Authors\Shinichi.png`） |
| `tests\MineSweeper.Core.Tests\` | 整个目录：360 个测试（20 个测试文件），是文档里所有质量数据的出处 |

整个仓库**不含任何第三方图片与音频素材**（主题是矢量自绘、音效是代码合成），这也是工程的红线之一。

### 12.2 绝不能提交的

| 路径 | 原因 |
|---|---|
| `HANDOFF.md` | 内部交接文档，**里面记录的东西会剧透玩家**（彩蛋位置等）。已在 `.gitignore` 中 |
| `Fastrof.png` / `Shinichi.png`（仓库根目录） | 作者头像**原图** 1448×1448（2.4 MB / 1.3 MB）；运行时用的是 `Assets\Authors\` 里那两份 256×256，根目录这两张不参与构建 |
| `*.7z` / `*.zip` | 体积较大的分发包（含旧版快照，不要当基准） |
| `bin/` `obj/` `artifacts/` `data/` `.vs/` `.vscode/` `*.user` `*.suo` | 构建产物与本地环境，已在 `.gitignore` 中 |

> 建议再往 `.gitignore` 追加两条，避免误提交头像原图：
> ```gitignore
> /Fastrof.png
> /Shinichi.png
> ```

### 12.3 首次推送

```bat
cd <项目目录>
git init
git add .
git ls-files | findstr /I "HANDOFF Fastrof.png Shinichi.png .7z"   :: 必须无输出
git commit -m "扫雷 2.0 v1.0.0：首个公开版本"
git remote add origin https://github.com/Fastrof/Minesweeper2.0.git
git branch -M main
git push -u origin main
```

第 4 行是**安全检查**：只要它列出任何文件，就说明忽略规则没生效，**停下来别提交**。

### 12.4 发布前还要确认

- **版本号要一起改 6 处**：`src\MineSweeper.App\AboutInfo.cs` 的 `Version`（`1.4`）、
  `MineSweeper.App.csproj` 的 `<Version>`（`1.4.0`）、MSIX 商店清单与侧载清单的 `Identity Version`（发版时写 `1.4.0.0`；
  `artifacts/msix` 里现存的是 1.0 / 1.3 的历史产物）、`README.md` 与 `README.en.md` 的版本行；
- `AboutInfo.cs` 里另外两处：`RepositoryUrl` 已填、`License` 文案；
- `WhatsNewCatalog` **最前面**加本版条目（`UpdatePreferencesTests` 有守门测试，漏加会红）；
- `build.cmd` → 0 警告 0 错误；`test.cmd` → 360/360；
- 源码里**没有任何硬编码的绝对路径**（唯一的外部引用是 `MineSweeper.Core.Tests.csproj` 里指向 `..\..\src\` 的相对引用），
  所以别人 clone 到任意目录都能直接编译。

### 12.5 发布到 Microsoft Store（实测流程）

> **以 `artifacts` 为准**：下面每一步都能在 `artifacts/msix` 与 `artifacts/store-listing` 里找到对应产物或素材；
> 命令里的参数（清单路径、证书、密码、版本号）以那些文件的实际值为准，不在这里写死。
> `artifacts/` 已在 `.gitignore` 里排除，**不要提交**。

已上架的商店页：**https://apps.microsoft.com/detail/9NKR2M2ZDDL3**（`artifacts/msix` 里有 1.0.0.0 与 1.3.0.0 两代包）。

1. **先改版本号 6 处**（见 §12.4）并跑一遍 `test.cmd`。
2. **选清单（双身份）**：`artifacts/msix/manifests/` 下有两套——商店用 `AppxManifest.store.xml`
   （`Identity Name=ZackStudio.Minesweeper2.0`、`Publisher=CN=CB6927D3-B186-4563-A18B-666FAEA221D0`，
   **以 `signing/Minesweeper2.0-store-test.cer` 的 Subject 为准**；旧版 manifests 里曾把 A18B 误写成 A188，
   打包前务必用 `certutil -dump` 核对），
   GitHub 侧载用 `AppxManifest.sideload.xml`（`Publisher=CN=Fastrof、Shinichi`）。
   两套的 `Publisher` **必须与签名证书 Subject 完全一致**，否则安装报 `0x800B0109`。
   源码树里对应的项目内清单是 `Package.store.appxmanifest.xml` / `Package.sideload.appxmanifest.xml`。
3. **准备展开目录**：`artifacts/msix/stage13b/` 就是一份展开后的包内容，可直接对照 ——
   自包含发布产物（`MineSweeper2.exe` + 一百多个 Windows App SDK / .NET 运行时 DLL）、
   `Assets\`（各尺寸 Logo / Splash / StoreLogo）、`Views\*.xbf`，加上清单里的 `Executable` 与图标路径必须对得上。
   注意本工程是 **unpackaged 工程 + 手工清单**（`WindowsPackageType=None`），
   所以包里的依赖是**自包含**的、清单里**不声明** `Framework` 依赖；`EntryPoint` 固定 `Windows.FullTrustApplication`。
4. **打包**：用 Windows SDK 的 `MakeAppx.exe pack /d <展开目录> /p <输出>.msix /o`
   （通用写法，参数以实际目录与产物名为准）。产物命名沿用现有的 `Minesweeper2.0_<版本>_x64_store.msix`。
5. **签名**：用 `artifacts/msix/signing/` 里的证书——商店包用 `store-test.pfx`（对应 `Minesweeper2.0-store-test.cer`，
   指纹记在 `store-thumbprint.txt`），侧载包用 `MineSweeper2.0-GitHub.pfx` / `MineSweeper2.0-dev.pfx`
   （密码在 `pfx-password.txt`）。
   `SignTool.exe sign /fd SHA256 /f <证书.pfx> /p <密码> <包.msix>`。
   商店最终提交的包由 Partner Center 用商店证书重签；这里签过的 `_store_signed.msix` 只是为了本机验证。
6. **本机验证**：`Add-AppxPackage .\Minesweeper2.0_<版本>_x64_store_signed.msix` 安装后跑一遍，
   重点看①通知表头小图标（打包版由 `uap:VisualElements` 提供，unpackaged 下改不动的问题随之解决）、
   ②数据目录变成包的 `LocalFolder`（不再是 `%LOCALAPPDATA%\MineSweeper2\`）、③检查更新在商店版里不启用。
7. **提交商店**：Partner Center → 新建提交 → 上传签好的 `.msix`；
   商店素材在 `artifacts/store-listing/`（`BoxArt-1080x1080` / `BoxArt-2160x2160` / `Hero-1920x1080` /
   `PosterArt-720x1080` / `PosterArt-1080x1920` / `StoreLogo-300x300`）。
   提交前确认商店清单里的 `DisplayName` / `Description` / `PublisherDisplayName` 与商店资料一致。
8. **GitHub Release 线**：同一份代码用侧载清单 + GitHub 证书另打一个包，
   产物与随包证书的成品格式见 `artifacts/msix/github/`（`Minesweeper2.0_1.3.0.0_x64_signed.msix` +
   `MineSweeper2.0-GitHub.cer` + `INSTALL.md` / `安装说明.md`），一并挂到 Release 里。
9. **验证清单**：商店页可访问 → 能安装启动 → 通知/图标正常 → 存档落在包数据目录 → 卸载干净。

---

## 附录 A. 完整变更记录

> 从 1.0.0 往前每一版改了什么，按时间倒序。每条都尽量写清"根因"，
> 因为这些坑很容易在后续改动里重新踩回去。
>
> 索引：**§0.7 = v1.0.0**（首个公开发布版）、**§0.6 = v0.8**（声呐结果弹窗 + 启动铃声 + 踩雷弹窗）、
> **§0.5 = v0.7**（关卡 18 → 36 关）、**§0.4 = v0.6**（三个道具 + 全局统一库存 + "三选一"补给 + 历史记录）、
> **§0.2 = v0.5**、**§0.1 = v0.4**（输入 / 窗口 / 通知等修复）、**§0.3 = 最初版本**那一轮的缺陷修复。

### 0.8 v1.1：成就系统 + 检查更新（含自动下载安装包）

- **成就系统**：31 条成就、合计 1000 点，分累计 / 速度 / 关卡 / 道具 / 挑战五类，其中 **2 条隐藏成就**
  （未解锁前界面只显示「???」）。判定全在 Core 的 `AchievementRules`（纯函数、56 个单测），
  进度存 `achievements.json`（原子写盘、损坏降级、版本过新拒读）。
  界面：底部「🏆 成就」打开面板；解锁时发**一条** Toast —— 同时解锁多个也只发一条，
  否则会互相顶掉（所有通知共用一个槽位，见 §4.3）。
  速度类判定会把**历史最快成绩**也算进去，v1.1 之前就刷出成绩的老玩家不用重打一遍。
- **检查更新**：`Core/Updates` 负责版本比较与规范化、GitHub JSON 容错解析、24 小时限流策略、
  选包规则与 SHA256 校验（全部有单测）；App 层 `UpdateChecker` 只请求 GitHub 公开接口、
  **失败一律静默**（断网 / 超时 / 限流 / JSON 异常都只写 app.log），**商店版完全不启用**
  （打包版由 Microsoft Store 更新，也不该把商店用户引到 GitHub 去侧载）。
- **自动下载安装包**：`UpdateDownloader` 下载到 `updates\<版本>\`，带进度回调、SHA256 校验、
  幂等复用（已下好且校验通过就不重复下）、只保留最新一版（一个包 90+ MB，不能无限攒）。
  安装方式按打包状态分两种，这是平台约束不是取舍：
  **打包版**走 `PackageManager.AddPackageAsync`（必须带 `ForceApplicationShutdown`，也就是**会重启**，
  所以下载可以自动、**安装必须玩家点**，绝不悄悄杀进程）；
  **未打包版**没有包身份、无法给自己安装，只能把 `.msix` 交给系统安装程序，由玩家确认。
- **产品口径修正（重要）**：加了检查更新之后，程序**不再"完全离线"** ——
  它会在检查更新时访问 `api.github.com` 的公开接口（不带任何标识、只读版本号）。
  因此 README / 商店说明 / 公众号文案 / 新增的 `PRIVACY.md` 都改成了
  "仅在检查更新时访问 GitHub 公开接口，不发送任何个人信息，可在设置里关闭"。
  默认值：**自动检查开、自动下载关**（安装包 90+ MB，流量网络上自动下不合适）。
- **两个新自检钩子**（沿用 §11.4 的约定）：
  `SAOLEI_ACHIEVEMENTS=check|panel`（check 用一条合成事实跑通"判定 → 落盘 → 面板汇总"，
  panel 直接开面板用于截图）；
  `SAOLEI_UPDATE=check|download|panel|off`（check / download 真的联网；panel 用假数据核对排版；
  off 验证"关掉开关就一次都不发请求"）。
- **顺带两处小改动**：`BoardView` 新增 `ChordResolved` 事件（成就要判"一次 chord 展开多少格"，
  而 UI 拿不到 `ChordResult.OpenedCount`）；`AppSettings` 新增 `autoCheckUpdate` / `autoDownloadUpdate`
  两个纯新增字段（有默认值，不需要 +schemaVersion 迁移）。
- 版本号三处一起改为 **1.1**（`AboutInfo.Version`、csproj `<Version>1.1.0</Version>`、MSIX 清单 `1.1.0.0`）。
- **修复（玩家报的 bug）**：失败弹窗的「重新开始这一盘」实际会**换一张新盘**（只沿用参数、种子留空即随机），
  与按钮文案矛盾。现在拆成「重打这一盘」（同一张盘，走快照精确复现，标记练习局不计最快记录）
  与「换一张新盘」（原行为）两个按钮，并给 `SAOLEI_UI=defeat` 自检加了"雷位逐位一致"的护栏，防回归。
  细节见 §附录 A 决定 27 的 v1.1 补充。
- **修复（玩家报的 bug）**：锁定盘面下**第一次左键无论点在哪都会被挪到首点**，于是玩家看到"我点这里，却在那边展开一片"（设置里 `lockedBoard: true` 时每局都会遇到）。现在只有点到**雷**才挪，点到安全格就在那一格展开；雷位仍只由 seed 决定，确定性不变。新增 `BoardOpening` + `LockedFirstClickTests`（5 条）把这个区别钉住；其中一条专门钉"挪到的必须是最近的 0 格"。

### 0.7 v1.0.0：首个公开发布版

这一版没有加新玩法，做的是"**能安心公开**"这件事：

- **仓库公开**：关于页面的"开源地址"从占位文案"即将公开"改成真实链接
  （`AboutInfo.RepositoryUrl = https://github.com/Fastrof/Minesweeper2.0`）。
  关于页面现在显示 **版本 1.0** 与可点击的仓库地址。
- **版本号统一到 1.0.0**（三处一起改：`AboutInfo.Version = "1.0"`、csproj `<Version>1.0.0</Version>`、README）。
  exe 属性显示 `1.0.0.0 / 1.0.0`。
- **文档与实现对齐**：把文档里还停留在旧数据的说明全部改准 ——
  测试数（144 → **162**）、存档路径（`data\` → **`%LOCALAPPDATA%\MineSweeper2`**，v0.6 就已搬迁但文档没跟上）、
  以及"开源地址留空"那段过期描述。
- **文档拆分**：README 精简成"给人看项目"的版本；本文件承接全部开发参考与变更记录。
- **发布前的自查**：`HANDOFF.md`（含彩蛋剧透）与两张作者头像原图不进仓库，见 §12 的清单。

> 1.0.0 的含义是"**功能与文档都对齐、可以对外**"，不是"从此不再改"。
> 后面如果要继续加东西，从 §11 接手须知开始看。

### 0.6 v0.8：声呐结果弹窗 + 启动铃声 + 踩雷弹窗

四条玩家反馈，前三条都是"信息留不住 / 找不到入口"这一类问题。

#### 0.6.1 声呐结果改成"需要手动确认"的小窗（蓝色瞄准框不再承担记忆）

- **问题**：声呐的蓝色瞄准方框在"放下去"的一瞬间就消失了（`ExitAiming` 会退出瞄准态），
  玩家很容易忘记**刚扫的是哪一块**、那一块里有**几颗雷** —— 而这个数字本来就是要拿来做判断的。
- **做法**：瞄准框按原样不保留（它只是"将要扫哪里"的预览，留着反而挡棋盘）；
  改由 `BoardView.SonarUsed` 事件把扫描结果抛给窗口，弹一张**必须点「知道了」才关闭**的卡片，
  把区域写成"第 4~6 行 × 第 4~6 列（3×3）"这种可以照着找的坐标，并带上剩余数量。
- **Toast 照旧保留**（`sonar.result`）：它负责"当下这一下"的即时提醒，卡片负责"回头看"。
  两者信息一致，不存在重复打扰——Toast 会自己收起，卡片不会。
- 顺带一个连带修正：`ExitAiming` 会整块重绘（见 §0.2.1），所以瞄准框退出后**不会**在屏幕上留残影。

#### 0.6.2 启动铃声（程序启动 / 切换难度）

- 新增第 6 个音效 `SoundEffect.Startup`（`startup.wav`，570ms）：
  一段 G-C-E-G 上行旋律，比"胜利"低一点、短一点，避免和通关混淆。
  仍然**零素材**——`WavSynth` 代码合成（见 §7.3 的红线）。
- 播放时机：**程序启动**一次 + 点**初级 / 中级 / 高级**三个按钮各一次。
- 刻意**不响**的时机：关卡局、按种子复现、点笑脸重开、重开这一盘 ——
  这些操作本身已经有翻开/结算音效，再叠一声只会吵；它们也不算"换一张盘面"。
- 实现上走 `SwitchPreset()` 而不是直接调 `PlayStartupSound`，这样"按钮接没接上"是代码结构保证的。

#### 0.6.3 踩雷失败的弹窗（所有模式）

- **问题**：以前踩雷只有一条 Toast，想过"重开这一盘"得自己去找小黄脸按钮，想退出得去关窗口。
- **做法**：普通局踩雷后弹 `DefeatOverlay`，两个按钮：**重新开始这一盘** / **退出游戏**。
  - "重新开始这一盘"走的是笑脸按钮那条 `RestartCurrentGame()`，所以**关卡局重开同一关**、
    普通局沿用**这一局真实生效的**盘面尺寸/雷数/模式/锁定设置，不会掉回默认难度。
  - "退出游戏"就是 `Close()`，会正常走 `OnWindowClosed`（落盘进度与设置）。
- **关卡局不叠加**：关卡失败已有自己的结算提示与"重开这一关 / 返回游戏"，再弹一层没有意义。
- 新一局开始时（`AttachSession`）会顺手收起失败弹窗与声呐卡片，避免它们跨局残留。

---

### 0.5 v0.7：关卡从 18 关扩到 36 关

- 关卡表从 **3 章 × 6 关** 扩到 **6 章 × 6 关 = 36 关**（`Core/Levels/LevelTable.cs`）：
  - **第四章 专家**：30×16，130→180 雷（密度 27% → 37.5%）
  - **第五章 大师**：30×18，150→225 雷（盘面变大，靠时间目标拉开差距）
  - **第六章 宗师**：32×20，190→290 雷（本作最长的一批盘面，640 格）
- 新关卡一律关掉"首点扩展安全区"，种子另起一段（44xxx / 55xxx / 66xxx），保证互不相同、也不与旧关相撞；
  星级时间门槛按"每格 1.5~2 秒"重新标定，全部满足"挑战时间严于目标时间"。
- **道具规则收口**：`LevelDefinition.ItemsAvailable` 改成"**主线关卡恒为 true**" —— 道具是全局共用的，
  关卡不再单独发放（关表里的 `MagnifierCount` 降级为纯设计数据）。

---

### 0.4 v0.6：三个道具 + 全局统一补给 + 历史记录

#### 0.4.1 两个新道具

| 道具 | 效果 | 与放大镜的区别 |
|---|---|---|
| **声呐扫描** 📡 | 选一个 3×3 区域，**只告诉你里面有*几颗雷***，不告诉你位置 | 它**不改变棋盘任何状态**：不翻开、不插旗、不影响胜负、也不占"已获知信息"（同一区域可以重复扫，只是浪费道具）。它给的是一次性的数字，位置得自己推 |
| **时间冻结** 🧊 | 倒计时**暂停 20 秒**（`item.freeze.seconds` 可配） | 冻结期间 `ElapsedSeconds` **不增长**，所以计时显示与星级判定自动跟着暂停，不需要各处特判；只影响用时，不影响棋盘。不能叠加，到期自动恢复 |

物品栏三格正好一格一种（放大镜 / 声呐 / 冻结），各带计数徽章。
**格子只有在"没货"或"本局已结束"时才变暗** —— 不会因为"还没翻开第一格""计时没开始"就变灰
（那样玩家会误以为道具没解锁）；真的还用不了的原因由点击时的提示说明。

#### 0.4.2 奖励机制总览：每 20 颗雷"三选一"补给

普通模式与关卡模式**完全一致**（细节见 §0.4.5–0.4.8）：

- 每扫到 **20 颗雷**（正确插旗，沿用原有的 `correctMineFlagsTotal` 计数）**攒一次奖励**，
  弹窗让你在放大镜 / 声呐扫描 / 时间冻结里挑一个。
- 跨阈值只**攒次数**（`PlayerProgress.pendingRewards`），玩家领了才真正进物品栏；
  某个道具存满（各 3 个）时那一格被禁用，**奖励不会被白扣** —— 换一个即可。

#### 0.4.3 历史记录（新增）

从玩家**第一次翻开格子**（= 第一次开始扫雷）记起，累计：扫到的雷数、总用时、对局数、胜利数、翻开格数、通关关卡数。

- 存档是独立的 **`%LOCALAPPDATA%\MineSweeper2\history.json`**，与 progress / records / settings / levelprogress 各自独立，互不影响。
- 入口是底部按钮行的**「历史记录」**；干净存档下显示"还没有记录 —— 从你第一次翻开格子的那一刻起，这里开始记录"。
- **它只活在这一个文件里**：把 `history.json`（或整个 `%LOCALAPPDATA%\MineSweeper2` 目录）删掉，历史就从零开始 ——
  这既是需求要求的行为，也直接写在弹窗里告诉玩家。

#### 0.4.4 一条贯穿全局的约定：所有个人数据都在同一个数据目录下

解锁关卡（levelprogress）、账号进度与道具（progress）、最快记录（records）、设置与个性化（settings）、
道具与棋盘配置（config）、历史记录（history）、日志（app.log）、合成音效（sounds）——
**全部**落在同一个数据目录里：**`%LOCALAPPDATA%\MineSweeper2\`**（打包版走包的 `LocalFolder`）。
因此：**删掉这个目录就等于一切从头开始**。

- **第一次启动会自动搬迁**：老版本把存档放在 exe 旁边的 `data\`，v0.6 起第一次启动会把里面的文件搬到新目录，
  搬完删掉旧的 `data\`（新目录已有同名文件时以新数据为准，不覆盖；跨盘也能搬）。
- **数据不再跟着 exe 走**：好处是装到 `Program Files` 这类只读目录也能正常存档，玩家数据也不会混在游戏目录里；
  代价是"拷贝整个游戏文件夹"不再带着存档。要彻底重置就删 `%LOCALAPPDATA%\MineSweeper2`。
- **没有任何写死的绝对路径**：打包版用包的 `LocalFolder`、未打包用 `%LOCALAPPDATA%`，都是运行时算出来的。
- 仓库里 `.gitignore` 排除了 `data/`，玩家存档不会被提交到公开仓库。

唯一的例外：有一项内容**刻意**设计成只在本次运行内有效（关掉游戏就消失），所以它本来就不该进存档。

#### 0.4.5 不再需要玩家去设置里开道具
- 设置面板里的**"模式"下拉删掉了**。它原来要求玩家在"纯经典（无道具）/ 经典 + 道具 / 自定义棋盘"之间选一个，
  等于把"能不能用道具"丢给玩家决定。
- 现在一律按**经典 + 道具**开局：道具默认就可用，不需要任何开关；自定义棋盘也一直可用。
- `GameMode.ClassicPure`（无道具盘面）仍留在 Core 里并有单测，作为一项**能力**保留；只是界面不再提供它。
#### 0.4.6 普通模式与关卡模式共用同一套道具
- 关卡**不再每关另发道具**。原来关卡用的是一份临时的、独立于账号的物品栏（"关卡物品栏"），
  现在**关卡和普通局共用同一个物品栏**：在关卡里获得或消耗道具，直接改同一份 `progress.json`。
- 关卡定义里的 `MagnifierCount` 字段保留（关卡表数据完整、便于以后做关卡差异），但**不再参与分发**；
  关卡目标条改为显示玩家当前的三种道具存量（镜 / 呐 / 冰）。
#### 0.4.7 初次赠送：三种各 2 个
放大镜、声呐扫描、时间冻结**第一次进游戏各送 2 个**（`item.*.initialGift`，默认 2），只送一次（`initialGiftGranted` 落盘）。
#### 0.4.8 补给机制：普通与关卡完全一致
- 无论普通还是关卡，**每扫到 20 颗雷就自动弹出"三选一"补给窗口**，玩家自选补给 1 个。
- 某一种道具存满（各 3 个）时那一格被禁用，**奖励不会被白扣** —— 换一个即可。
- v0.6 曾把普通模式留成"跨阈值自动发放大镜"，v0.6 起取消这个分叉：两种模式行为完全一致。

---

#### 0.4.9 界面细节修正（同一版内的收尾）

- **状态栏数字对齐**：`剩余 010` / `用时 000` 原本比中文标签偏高 —— 那两个 `TextBlock` 没有 `VerticalAlignment`，
  默认 Stretch 时文字画在盒子顶部。补 `VerticalAlignment="Center"` 后与标签同一条中心线。
- **设置面板三个控件对齐**：`音效` 原本用 `ToggleSwitch` 的 `Header`，而 `Header` 会把文字渲染在开关**上方**，
  和 `主题` / `音量` 的"独立标签 + 居中"不是一种排法。改成三者都用独立的居中 `TextBlock` 作标签，
  并给 ComboBox / ToggleSwitch / Slider / 试听按钮统一补 `VerticalAlignment="Center"`。

---

### 0.2 v0.5：三条玩家反馈修复 + 两项新增（已做）

#### 0.2.1 修复

| # | 问题 | 根因 | 修复 |
|---|---|---|---|
| 1 | **用过放大镜之后，屏幕上留下一圈蓝色线框** | 蓝色瞄准框（`theme.AimingRing`，经典主题 `#1E63D8`）是按**当时的视口**画出来的，而它在可见期间视口会被改好几次：右键物品栏会让右侧预览面板弹出，棋盘控件随之从 649 宽缩到 369 宽并触发重新适应窗口（offset 181 → 41）；透视成功后 `ZoomToCell` 又会把视口挪到别处。用"当前视口"去算该失效哪一块，算出来的是**新位置**，框的旧像素留在旧位置上——那块区域再没人重绘 | 退出瞄准态时**整块重绘**（`ExitAiming`），不再做局部失效；另外凡是会改视口或控件尺寸的地方（`FitToWindow` / `ZoomToCell` / `ZoomAt` / `PanBy`）也一律整块重绘。这些时刻本来就不频繁，代价可忽略 |
| 2 | **关卡一开始就自己走了一步**：打开关卡时棋盘已经开了一片、计时也在走 | `GameSession.TryCreateLocked` 在**创建时**就 `Reveal(board.FirstClickIndex)`。当初这么做是因为锁定盘面的雷位按 seed 提前定死、不能"围着首点布雷"，只能预先翻开首点来保证安全 | ① `TryCreateLocked` 不再 Reveal：开局整盘未翻开、`Status=NotStarted`、计时为 0；② 首点安全改由 `Reveal` 保证——锁定盘面且尚未开始时，把这次 `index` **钉到开局算好的安全点**上（玩家点哪一格都是安全展开，盘面仍与 seed 一一对应，`RevealResult.Index` 返回真正翻开的那一格）；③ 第二次点击起不再钉定 |
| 3 | **底部按钮/关卡导航行看不见**（新增按钮时暴露） | 主界面第 1 行是 `*`，所以"界面装饰"预算算少了**不会把棋盘压小**，而是把底部控件按顺序挤出可视区——第三行最先受害，而且是被压到高度 **0**（不是消失，很难看出来）。更隐蔽的是：棋盘里是 Win2D 画布，会一直画到自己的内容边界，小盘被 `MaxFitCellSize(44)` 放大后比所在行还高，**格线直接画到控件外、把底部界面盖住**，加 `Clip` / `MaxHeight` 都挡不住 | ① `ChromeHeight` 同步加上新一行的预算（300 → 380）；② 新增 `BoardView.AvailableHeight`，由宿主在 `BoardHost.SizeChanged` 里告知"这一行真正有多高"，`FitToWindow()` 与 `CenterBoard()` 都用它，**让棋盘自己把格子算小**而不是靠裁剪 |

> 第 1 条留个教训：**任何"只在某个状态为真时才画"的叠加层，退出该状态时都必须把它的旧像素清掉**；
> 而"按当前视口去算旧位置"在视口会变的前提下是错的，这种情况直接用整块重绘最稳。
>
> 第 2 条是个取舍：锁定盘面的雷位不能随首点变，所以"点了 A 却从 B 展开"无法避免——
> 要么钉到安全点，要么玩家第一下就可能踩雷（对固定盘面等于这关没得玩）。选了钉定，
> 并让 `RevealResult.Index` 把这件事显式暴露出来，而不是静默发生。
>
> 第 3 条留了两句：**改底部控件就一定要回头改 `ChromeHeight`**；量布局时记得先
> `UpdateLayout()`——刚把 `Collapsed` 改成 `Visible` 的元素不强制布局就读到旧的 `0`。

#### 0.2.2 新增

**① 游戏内的「上一关 / 下一关」**

- 放在游戏界面里（根 Grid 的第 3 行，`LevelNavRow`），**只在关卡局显示**，不用退回关卡面板就能连着往下打；
  点它直接换关，**不弹面板**。关卡面板上只有「返回游戏」。
- **下一关要已解锁才可点**：第 1 关永远开放，其余需要上一关已通关，所以它平时是灰的；
  ToolTip 说明是"通关本关后解锁"还是"已经是最后一关了"，第一关的「上一关」同样是灰的。
- 只有主线 36 关显示这一行；其它情况整行隐藏。

**② 胜利结算小弹窗**

- 通关时弹一张主窗口内的模态卡片：大字星级 + 本局用时 + 最好成绩 +（破纪录时的）🏆 + 刚解锁的下一关，
  下面三个按钮 **重开这一关 / 下一关 / 返回游戏**。
- **失败不弹**：Toast 已经说清楚了，而失败会反复发生，每次都拦一个弹窗会把"重试一下就好"的节奏打断。
- 做成窗口内覆盖层而不是独立窗口：结算看的是"这一局"的成绩，贴在棋盘上比另开窗口更贴近上下文。

#### 0.2.3 顺带修掉的两个潜伏 bug

- **`LevelTable.Next()` 的边界**：它原来就是 `At(level.Index + 1)`，遇到序号为 **-1** 的关卡会算成 `At(0)` = 第一关，
  "下一关"于是莫名其妙变成 1-1。已在 `Next()` 里显式挡掉这类关卡（具体是哪种关卡见 `HANDOFF.md`）。
- **自检里的"造通关"其实一直没走通**：`RevealAllSafeCells()` 一次 `Reveal` 会挂起分帧展开，
  挂起期间 `GameSession` 拒绝后续 `Reveal`，所以循环只能翻开第一格；而且首次点击会被钉到安全点，
  按索引顺序循环会永远漏掉 `index = 0` 那一格，差 1 格判不了胜。现在先走掉首点、每格都 `PumpPendingFlood()`。
  另外自检直接调 Core 的 `Reveal`，绕过了 `BoardView`，所以要自己置 `_levelPlayerActed = true`，
  否则 `SettleLevel()` 会把这一胜当成"开局即胜利"丢掉。

#### 0.2.4 文案与界面一致性

- 键盘操作在 v0.4 已经**整块移除**，但 UI 文案没跟着改：设置面板的操作说明还写着
  "空格+左键平移 / Ctrl+0 适应窗口 / 方向键+回车 / 按 1 进入瞄准态（Esc 取消）"，两个空槽位 ToolTip 还写着"按 2 / 按 3"。
  现在全部只描述**实际存在**的鼠标操作。
- 无障碍帮助文本同步修正（原文写"滚轮缩放"，实际是 `Ctrl+滚轮` 缩放、裸滚轮纵向滚动），
  并补上"本棋盘只支持鼠标操作，不响应键盘"。

---

#### 0.2.5 收尾修复（同一版本的后续小修）

| # | 问题 | 修法 |
|---|---|---|
| 1 | **自检可能把"结算还没走完"读成最终结果**（出现 `结算弹窗：可见=Collapsed（期望 Visible）`、失败后反而 `Visible` 这类自相矛盾的日志） | `_gameOverHandled` 是在调用 `HandleGameOver()` **之前**置位的，万一一帧里结算中途退出，自检就会读到半成品状态。新增 `_levelSettleCompleted`（在 `SettleLevel` **末尾**置位），自检改为只认它 |
| 2 | **锁定盘面的分享串复现少了一步**：复现出来是一张全未翻开的盘，而非锁定分享串会立刻呈现开局（两条路径行为不一致） | 锁定分支也 `Reveal(firstClick)`（首点会被钉到同一安全点，结果确定），并在 `锁定盘面分享串可精确复现` 里补上"开局已翻开"的断言 |
| 3 | **结算遮罩没盖住关卡导航行**：卡片弹出时底部"上一关 / 下一关"仍然亮着、可点 | `SettlementOverlay` 的 `Grid.RowSpan` 3 → 4（根 Grid 现在是 4 行） |
| 4 | 文档：公开的 README 里出现了只应出现在内部交接文档里的内容 | 细节挪进 `HANDOFF.md`，README 改成中性说法 |
| 5 | `.gitignore` 的注释写得过于具体，等于在公开文件里提示游戏里有隐藏内容 | 改成"内部交接文档：只给开发者看" |

---

### 0.1 v0.4：玩家反馈的 8 个问题 + 1 条需求变更（已做）

| # | 问题 | 根因 | 修复 |
|---|---|---|---|
| 1 | Toast 通知不会自动收起来 | Warning / Error 用了 `duration="long"`（约 25 秒），玩家体感就是"挂着不走" | **一律 `duration="short"`**（约 7 秒自动收起），级别区分保留提示音。<br>注：当时顺手把 Error 的警报音换成了 `Notification.IM`，后来玩家反馈"关卡失败的铃声变了"，已**按原版改回 `Looping.Alarm`**（见 `AudioSourceOf`）——收不起是时长问题，跟音源无关，两件事互不影响 |
| 2 | 主窗口不跟棋盘匹配，界面里有一大块黑底 | ① 窗口尺寸写死 `1180×900`；② `BoardView` 构造时硬编码了一整块深色背景 `0x20,0x20,0x22` | ① 每次开局按"棋盘格宽 26 DIP + 界面装饰"算目标尺寸并夹到屏幕可用区内（`FitWindowToBoard`）；② 背景改为透明，交给窗口主题背景 |
| 3 | 前一关没扫完，下一关却自动解锁 | 两个原因叠加：① 胜利条件是"翻开所有非雷格"，**并不要求把雷标出来**，所以赢了之后画面上还留着一堆未翻开格，看起来像"没扫完"；② 锁定盘面创建时会替玩家翻开首点，极小概率"开局即胜利"，那也会被当成通关 | ① 胜利时**自动把剩下的雷插上旗**（问号也换成旗），剩余雷数归零——原版也是这个表现；② 结算时若玩家**一次都没操作过**就不算通关、不解锁 |
| 4 | 左键点的位置和实际生效的格子对不上：要么没反应，要么翻开了别处 | **左键是"松手才判定"、而且用的是松手时的坐标**：手一抖 → 翻开隔壁格；抖动超过 8 DIP → 被当成拖拽直接丢弃 | **鼠标/笔改回"按下即生效"，并用按下的坐标判定**；8 DIP 阈值只留给触屏。另外在失焦时清空输入状态，避免"按住空格时窗口被切走"导致之后每次左键都变成平移 |
| 5 | 点开了第 4 条之后，**偶尔还是点不开、也插不了旗**（旧问题残留） | 两类原因：① **瞬时输入状态被卡住**——中键拖拽到控件外松手时收不到松手事件，`_panning` 一直为真，之后每次左键都被当成"继续平移"；按住空格时窗口被切走会丢掉 KeyUp，`_spaceHeld` 一直为真。② 小盘（9×9）缩在一大片空白正中间，**点到棋盘外面当然没反应** | ① `PointerCaptureLost` 与 `LostFocus` 都清空输入状态，`PointerMoved` 里发现按键其实已松就自愈，新的左键按下也顺手清残留；② 适应窗口时**允许把格子放大**（新增 `BoardTheme.MaxFitCellSize = 44`），小盘会放大到接近铺满，格子更大也更好点 |
| 6 | **窗口最大化时点笑脸，最大化会被还原掉** | v0.4 那一轮新加的 `FitWindowToBoard()` 在每次开局都 `AppWindow.Resize`，把最大化状态顶掉了（**这条是同一轮修复自己引入的回归**） | `FitWindowToBoard()` 开头先看 `OverlappedPresenter.State`：只要不是 `Restored` 就**直接返回、绝不动窗口尺寸** |

> 第 4、5 条是同一类问题（"点哪儿"和"开哪儿"脱钩），第 5 条是第 4 条修完后的残留：第 4 条治的是"判定时机"，
> 第 5 条治的是"输入状态被卡住"和"棋盘外没有可点区域"。
> 另外新增了点击诊断开关 `SAOLEI_INPUT=1`（见 §11.4），万一还有对不上的情况，`app.log` 里能直接看到每次点击的坐标与算出来的格子。
>
| 7 | **失败后点小黄脸重开，会掉回"初级 10 雷"**（玩 40 雷也一样回到 10 雷） | 笑脸按钮的处理函数写死了 `StartPreset(DifficultyPreset.Beginner)` | 笑脸改成"**重开这一局**"（`RestartCurrentGame`）：关卡局重开同一关；普通局沿用**当前这一局真实生效的**盘面尺寸 / 雷数 / 模式 / 锁定盘面，种子取种子框（留空 = 一张新的随机盘）。参数取自 `GameSession` 而不是界面控件，所以"开局之后又改了模式或锁定开关"也不会影响重开 |
| 9 | **Toast 通知跟不上操作**：触发一条（如"chord 未执行"）之后紧接着触发另一条（如"踩雷失败"），后一条要等前一条关掉才推送/响铃 | 两个原因叠加：① 每条通知都占**独立槽位**，而 Toast 是**排队**的——一条不消失，下一条就只能等；② 旧版本没有槽位概念，玩几局就在操作中心里**积压**了一串没看完的通知（实测积压了 11 条），新通知全排在它们后面 | ① 所有通知共用同一个 `Tag`+`Group`（`game/minesweeper2`），新通知**替换**旧通知而不是排队；② **启动时清空本作遗留的通知**（`RemoveAllAsync`），把历史积压清掉。实测：连发两条 → 操作中心只剩 1 条，且是后发的那条 ✓ |
| 8 | **彻底禁用键盘操作**：不能有那个蓝色的小方框，棋盘只能用鼠标（**需求变更**，不是 bug） | 原本按开发文档 §3.2 实现了完整的键盘操作（方向键/回车/F/1/Esc/Ctrl+0），焦点格靠自绘的蓝色方框标出来 | 键盘相关代码**整块删除**：`IsTabStop`、`KeyDown/KeyUp`、焦点格 `_focusIndex`、`MoveFocus`、`EnsureFocusVisible`、键盘播报、`Focus()` 调用、Windows 的 LiveRegion 设置，以及渲染里的焦点框。连带把"空格+左键平移"也去掉（键盘没了它就没有触发者），并把**已经变成死数据**的调色板键 `focusRing` 从 `BoardPalette` / `BoardTheme` / 测试键清单里一并移除（20 → 19 个颜色键）。无障碍 peer 保留但改为**只读**：去掉 `IInvokeProvider` 与聚焦模式，只朗读 |

> 第 6 条提醒一件事：**改窗口尺寸的代码一定要先看窗口状态**。最大化/全屏时任何 `Resize` 都会打断玩家的使用。
>
> 第 9 条要记住一句：**Toast 是排队的，不是"谁后到谁先显示"**。想让"操作 → 响铃"同步，
> 就必须让所有通知共用一个槽位（替换语义），并且把历史积压清掉。详见 §4.3。
>
> 第 7 条留下一个小坑：**按钮的语义要按"玩家以为它干什么"来写**。小黄脸在扫雷里是"重开这一局"，
> 不是"回到初级"。凡是"重开 / 再来一局"这类入口，参数都该来自**正在运行的那一局**，
> 而不是界面上的默认值或当前控件值。
>

> 第 8 条提醒另一件事：**这是对开发文档 §3.2 的明确偏离**。文档要求键盘与无障碍操作，项目所有者要求纯鼠标，
> 以后者为准。要恢复键盘操作的话，`BoardView` 里被删掉的那几块（键处理 + 焦点格 + 渲染焦点框）
> 需要照着文档重写，调色板也要把 `focusRing` 键加回来。
>
---

### 0.3 最初版本：缺陷修复（相对最早的原型）

| # | 缺陷 | 影响 | 修复 |
|---|---|---|---|
| 1 | **翻开后只重绘被点击的那一格** | `Reveal` 可能翻开一整片，但 UI 只失效 1 格 → 屏幕上只开一格，其余已逻辑翻开的格子仍画成未翻开；再点它们会被"该格已翻开"拒绝 → **玩家感觉"点了没反应"** | Core 新增改动区间回传（`FloodFillOperation.Min/MaxRevealedIndex` + `GameSession.ConsumeLastAffectedRange`），UI 按区间精确重绘；胜利/失败走整屏重绘 |
| 2 | **左右键同按 = chord 是死代码** | `OnPointerPressed` 先处理右键并 `return`，双键判定永远不可达 → 实际变成**插旗** | 把双键判定提到右键分支之前 |
| 3 | **布雷时每格分配一个迭代器** | `ComputeAdjacentMines` 用 `yield return` 版邻格遍历，4096×4096 = 1677 万次堆分配，首次点击卡死数秒 | 改用零分配的 `GetNeighbors8(Span<int>)`（与洪水填充一致） |
| 4 | **分帧展开被 200ms 定时器限速** | 4096×4096 空盘要等约 67 秒，期间输入被锁 | 定时器 200ms → 16ms；`Tick` 按 8ms 时间片连续推进 |
| 5 | **首点前放大镜不置灰** | `IsMagnifierUsableNow` 算了却没人绑定（死属性），与 §5.5 ① 要求的视觉不符 | 物品栏不透明度改绑该属性 |
| 6 | **纯经典模式也会消耗"首次赠送"** | 默认模式第一次启动就把赠送标记掉，之后切带道具模式拿不到 | 赠送只在"本局启用道具"时发放（`GrantInitialGiftIfItemsAvailable`），ViewModel 不再无条件赠送 |
| 7 | **`records.json` 不是原子写盘** | 崩溃/断电可能写坏记录文件（`progress.json` 早就有保护，记录没有） | 抽出 `AtomicFile`，两个存档文件共用 |
| 8 | **种子分享串只能生成、不能导入** | `TryParseShareCode` 是死代码，§6.3 的"分享同一盘"实际不可用 | 新增 `GameSession.TryReplayShareCode`（精确复现）+ 设置面板"按分享串复现盘面"入口 |
| 9 | 透视缺少 §5.4 要求的淡出反馈 | 视觉缺一项 | 结算后画高亮框，由 16ms 定时器驱动 1 秒淡出 |
| 10 | 帮助文案与实现不符 | 写着"按 1 或空格进入瞄准态"，但空格被用作平移修饰键 | 文案改为"按 1 进入瞄准态"；该快捷键后来随"全面禁用键盘"一起移除（见 §0.1 第 8 条），设置面板的操作说明也一并清掉了键盘相关说法，只描述鼠标操作 |
| 11 | `.cmd` 中文乱码、且不传递失败退出码 | cmd 默认 936 代码页读 UTF-8 脚本 | 加 `chcp 65001`，并补 `exit /b` 正确回传退出码 |
| 12 | 脚本写死 `E:\dotnet`、README 路径过期 | 换机器就跑不起来 | 脚本改为依次探测常见 SDK 位置，找不到就用 PATH |
| 13 | 窗口尺寸变化后棋盘停在旧位置 | 提示条收起/窗口缩放后棋盘偏上偏左（实测上下留白差 27 DIP） | 玩家没手动调过视图时，尺寸变化即重新适应窗口 |
| 14 | **M2 验收标准不达标** | 1000×1000 全景实测 **332 ms/帧（3 FPS）**，文档要求的"不卡顿"完全不成立 | 远景改"同色行程合并 + 竖直色带合并 + 关闭抗锯齿" → **14.4 ms/帧（70 FPS）**，见 §6.9 |
| 15 | 远景有横向条纹 | 格宽 0.52 DIP 时逐格矩形边缘抗锯齿混出横纹 | 同上：合并 + 纯色块关抗锯齿后消失 |
| 16 | 问号颜色不符合文档 | §4.1 要求"黄色问号"，实现是深蓝 | 经典主题改成黄色 `#E0A800`，并加测试防回退 |

---

## 13. v1.2 / v1.3 变更要点（补充）

**v1.2（更新设置与修复）**
- 自动检查频率：`UpdateFrequency`（每次启动 / 每天 / 每 3 天 / 每周 / 每 2 周 / 每月）＋ `UpdateSchedule.ShouldAutoCheck`。
- 暂停更新：`UpdatePause`（暂停到指定日期；暂停期间不发任何请求）；暂停期手动点「检查更新」= 立即恢复自动检查。
- 「本版新增」：`WhatsNewCatalog`（1.0 / 1.1 / 1.2 / 1.3 各一条；升版本必须同时加条目，`UpdatePreferencesTests` 有守门测试）。
- 修复：锁定盘面下"点哪一格就开在哪一格"（`BoardOpening.FindNearestZeroCell`）；失败弹窗拆成「重打这一盘」（练习局，不计最快记录）与「换一张新盘」。

**v1.3（中英双语 + 界面颜色主题）**
- 语言表在 **Core**：`Localization/LanguageCatalog.cs`（当前只有 `zh-Hans` 与 `en` 两种）＋ `Localization/Strings.cs`（`SetLanguage` / `Get` / `ResolveFor` / `TranslateLiteral` 反查）＋ `Localization/StringTables.cs`（约 330 键/语言）。
- 翻译的三种接入方式：① 代码里直接 `Strings.Get("key", …)`（整句模板，禁止拼句）；② `Strings.TranslateLiteral(原文)` —— 用于 Core 里已经存在的整句常量与数据（关卡名、主题名、道具名、版本说明、更新原因等）；③ `Services/XamlLocalizer.cs` —— 扫描可视化树，翻译 XAML 静态文案与 **ToolTip**（主窗口在启动/切语言时扫，两个「关于」窗口在内容 `Loaded` 后扫）。
- Toast 出口统一翻译（`ToastNotifier`），所以 Core 抛出的整句文案只要入了表就自动英文化。
- 界面颜色主题：`Themes/UiThemeMode.cs`（`System/Light/Dark` ＋ 容错解析）＋ **`BoardContrast`**：按 WCAG 相对亮度把棋盘底色推到与背景的亮度差 ≥ 0.12（保持色相、幂等），保证深色 UI 下棋盘不会"糊"进背景（`UiThemeTests` 有"任何棋盘主题 × 任何界面模式都必须分得开"的守门测试）。
- 两个「关于」窗口是独立 `Window`，不继承主窗口主题：创建后调用 `ApplyThemeToWindow`（覆盖 `Panel`/`Control`/`Border` 三种根元素），切换主题时也会刷新已创建的窗口。

---

## 14. v1.4 变更要点（金币商店与放大镜 2×2）

**金币经济**
- 每扫出一颗雷 **+1 金币**（与"正确标记"同一事件，纯经典模式也照赚）——`GameSession.ToggleFlag` 里调 `Coins.EarnForMine()`，实现在 `Core/Items/CoinWallet.cs`。
- 价格：放大镜 **30** / 声呐扫描 **20** / 时间冻结 **20**。默认值在 `CoinWallet`；实际售价可被 `config.json` 的 `shop.*.price` 覆盖，而扣款只认配置价，避免"显示 50、实扣 30"。
- 金币与买到的道具**只在本局有效**：每局重新开始清零，关卡模式每到新的一关也清零。钱包挂在 `GameSession` 上（新局 = 新钱包，结构保证），买到的数量在 `ItemInventory.SessionPurchases`，消耗时优先扣本局买的。

**商店入口与购买**
- 棋盘右上角浮层「🛒 商店 + 🪙 金币数」，**不占布局**（不会把棋盘挤小），扫雷过程中随时打开/关闭。
- 购买走 `GameSession.TryBuyItem`，返回 `ShopPurchaseResult`（买不起 / 满仓 / 本局不启用道具 / 本局已结束 / 未知道具），UI 按结果给文案与按钮态；满仓与买不起**一分不扣**。

**放大镜 2×2**
- 透视范围从 3×3 改成 **2×2**，**点中的格子是左上角**（贴边按棋盘边界裁切，最多 4 格、边角更少）。
- 用 `Core/Items/AreaShape.cs`（宽 × 高 + 锚点）替代"半径"：半径只能表达奇数边长，2×2 表达不了；声呐仍是 3×3 居中（`AreaShape.Square`）。

**取消「每 20 雷三选一」**
- `GameSettings.ChoiceRewards`、`pendingRewards`、`ClaimReward` 与三选一弹窗一并移除；阈值 20 只保留给统计展示。

**ItemConfig schema v1 → v2 与存档**
- `schemaVersion` 1 → 2：`item.magnifier.maxHold` 3 → **9**；`item.magnifier.radius` → **`item.magnifier.width` / `item.magnifier.height`**（默认 2/2）；新增 `shop.magnifier.price` / `shop.sonar.price` / `shop.freeze.price`。
- 迁移在 `Persistence/ItemConfigStore.Migrate`：老文件里的 `maxHold: 3` 会被原样读进来（只靠属性默认值兜不住），必须按 `schemaVersion` 显式提到 9，否则商店对老玩家永远"满仓买不了"；迁移后立刻写回配置。`progress.json` 的旧 `pendingRewards` 字段保留兼容、不再增长。

**测试与版本号**
- 测试从 329（v1.3）增到 **360**（v1.4 新增 17 条，分布在 `ItemTests` / `PersistenceTests` / `UpdateTests` / `AreaShapeAndCoinTests` / `HistoryTests`，文件数仍是 20）——逐文件数字以 `test.cmd` 输出为准。
- 版本号 6 处一起改：`AboutInfo.Version`（1.4）、`MineSweeper.App.csproj` 的 `<Version>`（1.4.0）、MSIX 商店/侧载清单的 `Identity Version`（1.4.0.0）、`README.md` / `README.en.md`；另加 `WhatsNewCatalog` 的 1.4 条目（有守门测试）。

**站点与文档同步**
- README / README.en / 站点 `docs/index.html` / `docs/en.html` / `HANDOFF.md` 同步到 v1.4：放大镜 2×2、金币商店、测试数 360、商店已上架链接；本文件补 §12.5（发版流程）与本章。

### 14.1 本轮修复与实测（v1.4 收尾）

- **商店扣款**严格按 `config.json` 的 `shop.*.price`（默认 30 / 20 / 20），**不是硬编码常量**；改价后界面显示与实扣一致。
- **检查更新**：实际间隔只由设置里的频率档位决定（"每次启动"就是每次启动都查），**不再被固定 24 小时的二次拦截**（`24 小时限流`只是 v1.1 的旧行为，v1.4 已调整）。
- **存档 / 配置**：`config.json` 也走**原子写**；损坏会备份 + 重建默认；`schemaVersion` 过新时**加载后拒绝写回**，避免旧版本覆盖新版存档。
- **放大镜**：2×2、点中格为左上角；所有面向玩家的文案（含 ToolTip、商店描述）都已同步成 2×2。
- **状态栏**不再显示"每 20 雷补给 x/20"，改为显示**本局金币**；"道具补给"相关文案已移除。
- **商店入口 / 标题 / ToolTip** 已接入中英双语；隐藏关的失败 / 开始提示不再说"同一关盘面固定"。
- **`ItemUsed`** 现在覆盖三种道具（声呐 / 冻结 / 放大镜各自独立标记），成就判定正确。
- **关卡面板**列数自适应修复（随窗口宽度在 3 列 ↔ 2 列之间切换）；两个「关于」窗口在 **150% DPI** 下不再偏大。
- **界面颜色「跟随系统」**：以前只在启动时算一次窗口背景与棋盘底色，运行中切换 Windows 深浅色会"控件变了、背景没变"；现在订阅 `RootGrid.ActualThemeChanged` 实时刷新（显式浅色/深色不受影响）。
- **自检不再污染主题设置**：`SAOLEI_UITHEME=light|dark|system` 只用于截图核对，`OnUiThemeChanged` 在强制模式下不再写 `settings.json`（否则自检时打开设置/切语言可能把临时主题永久保存）。
- **实测**：`dotnet build MineSweeper2.sln -p:Platform=x64` → **0 警告 0 错误**；`dotnet test` → **360 / 360 通过、0 失败**。
