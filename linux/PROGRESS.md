# Linux 端 v1.5 功能对齐 — 交付报告

> 基线：Windows v1.5（`043c577`）｜ 完成日期：2026-10-21
> **隔离约束：Windows 仓库零改动**（`git status` 为空，HEAD 未变）

---

## 结论：v1.5 功能清单已全部对齐

| # | v1.5 功能 | Linux 端状态 | 证据 |
|---|---|---|---|
| 1 | 经典玩法（首点安全/三态/chord/胜负/计时/剩雷） | ✅ | Core 规则，`GameCoordinator` 转发 |
| 2 | 输入手势（缩放/平移/双击与中键 chord） | ✅ | `BoardControl`，含左右同按 chord |
| 3 | 8 份存档（XDG 路径） | ✅ | `~/.local/share/minesweeper2/`（实测落盘） |
| 4 | 设置面板 | ✅ | `05-settings.png` |
| 5 | 主题（4 套 + 界面深浅色） | ✅ | Core `BoardPalette`；`13-dark-theme.png` |
| 6 | 中英双语 | ✅ | Core 409 键 + 兜底；`14-english.png` |
| 7 | 36 关关卡模式 + 结算 | ✅ | `06-levels.png`、`SettlementOverlay` |
| 8 | 道具（放大镜 2×2 / 声呐 3×3 / 冻结） | ✅ | `11-aiming-magnifier.png` |
| 9 | 金币商店 | ✅ | `07-shop.png` |
| 10 | 31 条成就 | ✅ | `08-achievements.png` |
| 11 | 记录 + 历史 | ✅ | `09-records.png` |
| 12 | 关于窗口 | ✅ | `10-about.png` |
| 13 | 检查更新 | ✅ | `UpdateService`（GitHub Release，只提示） |
| 14 | 通知 | ✅ | `notify-send`（实测可用） |
| 15 | 音效 | ✅ | `paplay` + Core 合成 6 个 WAV |
| 16 | 无障碍 | ✅ | 棋盘 `Pane` + 状态摘要（实测输出） |
| 17 | 本版新增 | ✅ | `00-whatsnew.png` |
| 18 | CI | ✅ | `.github/workflows/linux.yml` |

---

## 架构

```
MineSweeper2.0-Windows/linux/
├── src/MineSweeper.App.Linux/
│   ├── AppHost.cs                    组合根（存档 + 7 服务 + 编排）
│   ├── Program.cs                    入口 + --screenshot
│   ├── Screenshots.cs                无头截图（15 张）
│   ├── Services/
│   │   ├── AppPaths.cs               XDG 路径 + 日志
│   │   ├── Storage.cs                8 份存档装配
│   │   ├── GameCoordinator.cs        开局/动作/结算/分帧
│   │   ├── AchievementService.cs     移植自 Windows
│   │   ├── ThemeService.cs           调色板 + 对比度保障
│   │   ├── LocalizationService.cs    409 键 + 兜底
│   │   ├── SoundService.cs           paplay / pw-play / aplay
│   │   ├── NotificationService.cs    notify-send
│   │   └── UpdateService.cs          GitHub Release 检查
│   ├── Controls/
│   │   ├── BoardControl.cs           自绘棋盘 + 输入 + 瞄准态 + 远景合并
│   │   └── BoardAutomationPeer.cs    无障碍（只读 Pane）
│   ├── Rendering/{BoardViewport,LinuxBoardTheme}.cs
│   └── Views/
│       ├── MainWindow.axaml(.cs)     状态栏 + 棋盘 + 按钮 + 覆盖层宿主
│       └── Overlays/
│           ├── SettingsOverlay.cs    设置
│           ├── LevelOverlay.cs       36 关
│           ├── SettlementOverlay.cs  结算
│           ├── ShopOverlay.cs        金币商店
│           ├── AchievementsOverlay.cs 31 条成就
│           ├── RecordsOverlay.cs     记录 + 历史
│           ├── AboutOverlay.cs       关于
│           └── WhatsNewOverlay.cs    本版新增
├── packaging/{pack-deb.sh, *.desktop, *.metainfo.xml}
├── scripts/{build.sh, run.sh}
├── .github/workflows/linux.yml
└── dist/minesweeper2_1.5.2_amd64.deb
```

---

## 实测数据（最终版）

| 项 | 值 |
|---|---|
| 构建 | **0 错误**，3 个警告（Bitmap.Save 过时 ×1、可空性 ×1、未用事件 ×1） |
| 无头截图 | **15 张**（初始/翻开/插旗/高级/设置/关卡/商店/成就/记录/关于/瞄准/百万格/深色/英文/本版新增） |
| 百万格远景 | 1,000,000 可见格 → **2 次绘制 / 9.07 ms** |
| `.deb` | **35 MB**（解压 102 MB），lintian **零告警** |
| 依赖 | libc6/libstdc++6/libgcc-s1/zlib1g/libx11-6/libice6/libsm6/libfontconfig1/libfreetype6/fonts-dejavu-core |
| SHA256 | `23A217B60AECCC81B271506FC64EF9509D4B481874005E61D6D41B747EA5A940` |
| 无障碍 | `扫雷棋盘 9×9，10 颗雷，已翻开 0 格，剩余雷数 10，准备中` |
| 存档 | 8 份 JSON + 6 个音效 WAV |
| **Windows 仓库** | **零改动**（`git status` 空，HEAD `043c577`） |

---

## 使用方法

```bash
# 安装（Debian/Ubuntu）
sudo apt install ./minesweeper2_1.5.2_amd64.deb
minesweeper2

# 或直接运行（不安装）
cd ~/artifacts/ms2linux/deb/publish && ./Minesweeper2

# 无头截图（CI/视觉回归）
./Minesweeper2 --screenshot /tmp/shots
```

```powershell
# Windows 侧一键启动（WSLg）
D:\zack\Desktop\MineSweeper2.0\MineSweeper2.0-Windows\linux\run-linux.cmd
```

---

## 已知差异与后续

| 项 | 说明 |
|---|---|
| 窗口标题栏 | WSLg 用 Weston 标题栏，非自绘（Linux 原生桌面正常） |
| 字体 | 棋盘数字用 `DejaVu Sans`（Windows 用 Consolas）；`.deb` 声明依赖 `fonts-dejavu-core` |
| 双语兜底 | 少数新增面板文案在 Core 语言表里没有对应 key，用中英兜底（显示正常，但不在语言表内） |
| 无障碍覆盖 | 棋盘级 Pane（状态摘要）；逐格虚拟节点未做（Windows 端最多 400 格） |
| 更新 | 只提示新版本 + 打开 GitHub 页面；`.deb` 不做自动安装 |
| CI | workflow 已写，需推送到 GitHub 后才能实际运行 |

---

## UI 重做（对照 Windows v1.5）

### 主窗口布局（与 Windows 端 1:1 对齐）

| 位置 | 内容 |
|---|---|
| Row 0 状态栏 | 剩余雷数卡片（剩余 010/10）· 状态文字（可伸缩 + 省略号）· 笑脸 · 用时卡片 · **物品栏 3 格 48×48（右下角数量角标）** · 提示按钮（按局型显隐） |
| Row 1 棋盘区 | 棋盘（带边框）· **右上角商店浮层（不占布局）** · **右侧放大镜信息面板 280 宽（瞄准时出现）** |
| Row 2 操作区 | 关卡目标条（关卡局显示）· 难度行 · 功能行（设置/复制种子/历史/成就/关于游戏/关于作者 + 种子）· **内联设置面板（可折叠）** |
| Row 3 关卡导航 | ◀ 上一关 / 下一关 ▶（关卡局显示，按解锁状态置灰） |

窗口尺寸按 `26 DIP/格 + 装饰 190×420` 自动适配，夹到屏幕 94%，下限 700×540（与 Windows 端同一套算法）。

### 关卡面板（重做）

- 6 章分组，每关一张 **200×92 卡片**：编号+名称、盘面与星数、星级门槛、最好用时
- 未解锁显示 🔒 并置灰
- 顶部汇总「已通关 x/36 · 星数 y/108」+ 返回游戏
- 彩蛋解锁后，最下面出现 **隐藏章节「EX-1 卡布奇诺」**（每次打开重摇盘面，不落盘）

### 关于窗口 + 彩蛋（对照 HANDOFF.md）

| 彩蛋 | 触发 | 效果 |
|---|---|---|
| 1 | 关于游戏 · 连点**版本号 10 下** | 隐藏卡片「🎉 隐藏彩蛋 · 十连击」+ 和弦音效 |
| 2 | 关于作者 · 连点 **Fastrof 头像 3 下** | 名字旁出现 😎 + 卡片「卡布奇诺时间」+ 和弦音 |
| 3 | 关于作者 · 连点 **Shinichi 头像 5 下（未先触发 2）** | 「😴 来早了一步」小彩蛋 |
| 4 | 关于作者 · 已触发 2 后连点 Shinichi 5 下 | 🐛 出现 + **解锁隐藏关 EX-1** + 胜利音 |

彩蛋**全程无提示**（无 ToolTip、无悬浮变色），只在 `app.log` 留一行 `[记录/Info]`，绝不弹通知。

### 设置面板（补齐 Windows 端项目）

界面颜色 / 语言 / 棋盘主题 / 自定义宽高雷 / 种子开局 / **分享串复现** / 首点扩展 / 失焦暂停 / 无猜 / 音效开关 / 音量 / 试听 / 检查更新 / 本版新增。
### 补充完成（对齐 Windows 端的最后一批细节）

| 功能 | 状态 |
|---|---|
| **失败卡片**（💥 踩到雷了 + 重打这一盘 / 换一张新盘 / 退出游戏） | ✅ 与 Windows 端同文案同结构；「重打这一盘」走 `CreateReplay` 精确复现同一张盘并标记练习局 |
| **声呐结果卡片**（区域/中心/剩余数量，手动「知道了」关闭） | ✅ |
| **关于窗口独立化** | ✅ 两个独立 `Window`（不可缩放/最大化/最小化，居中于主窗口），内容仍是可复用的 Overlay 控件 |
| **放大镜预览内容** | ✅ 瞄准时右侧面板列出区域坐标与内容（`MagnifierReveal.ToPanelText`） |
| **关卡卡片列数** | ✅ 卡片 240 宽，窗口宽时 3 列、窄时自动换行 |