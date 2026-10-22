# MineSweeper 2.0 · Linux（Avalonia）

这是扫雷 2.0 的 **Linux 前端**，与 `..\MineSweeper2.0-Windows`（WinUI 3）**并列**，
共享同一份 `MineSweeper.Core` 规则层。

> **状态：技术预览（Spike）**。当前能玩经典模式 + 三档难度，其余功能按阶段补齐。

---

## 1. 最重要的约定：不碰 Windows 仓库

本目录是**独立工程**，与 Windows 仓库的关系只有一条：**只读地编译它的 Core 源码**。

| 机制 | 做法 |
|---|---|
| Core 复用 | `MineSweeper.App.Linux.csproj` 里用 `<Compile Include="..\..\..\MineSweeper2.0-Windows\src\MineSweeper.Core\**\*.cs" />` **链接编译**，不复制、不修改源码 |
| 构建产物 | 统一用 `--artifacts-path "$HOME/artifacts/ms2linux"`，`obj/bin` 全部落在 Linux 侧磁盘 |
| 结果 | **Windows 仓库目录零写入**（跑完 `git status` 仍然是干净的） |

将来若改成"多平台单仓"，把那段 `Compile Include` 换成 `ProjectReference` 即可，其余不用动。

---

## 2. 环境要求

| 项 | 要求 |
|---|---|
| OS | Ubuntu 22.04+ / Debian 12+（`libc6 >= 2.35`），x86_64 |
| .NET SDK | **10.0.x**（开发用；运行 `.deb` 不需要，因为包是 self-contained） |
| 显示 | X11 或 Wayland（WSLg 也可） |
| 打包 | `dpkg-deb`、`strip`（Ubuntu 自带） |

> WSL 里如果 `dotnet` 报 `Failed to create CoreCLR`，是因为源码/SDK 放在 `/mnt/d`（DrvFs）。
> 把 SDK 装到 `~/.dotnet`、源码放到 Linux 原生文件系统即可。

---

## 3. 构建

```bash
# 在 WSL / Linux 里
export DOTNET=$HOME/.dotnet/dotnet        # 或 PATH 里的 dotnet
scripts/build.sh                          # Release 构建，产物进 ~/artifacts/ms2linux
```

手动等价命令：

```bash
dotnet build src/MineSweeper.App.Linux/MineSweeper.App.Linux.csproj \
  -c Release --artifacts-path "$HOME/artifacts/ms2linux"
```

## 4. 运行

### Windows + WSLg（推荐：双击）

| 文件 | 作用 |
|---|---|
| `build-linux.cmd` | 一键构建（内部调用 `scripts/build.sh`） |
| `run-linux.cmd` | 一键启动窗口（自动检查 WSLg、自动找可执行文件） |

> 两个 `.cmd` 的提示信息**故意全用英文**：Windows cmd 默认 GBK 编码，
> 中文写进 `.cmd` 或在没 `chcp 65001` 的窗口里输出 UTF-8 都会乱码。
> 脚本里已经 `chcp 65001`，所以 WSL 侧的英文/中文输出都能正常显示。

### 命令行（PowerShell）

```powershell
# 方式 A：走 run.sh（最稳，不依赖 shell 变量展开）
wsl -d Ubuntu -- bash /mnt/d/zack/Desktop/MineSweeper2.0/MineSweeper2.0-Windows/linux/scripts/run.sh

# 方式 B：直接跑构建产物（注意用单引号，避免 PowerShell 展开 $HOME）
wsl -d Ubuntu -- bash -lc 'cd ~/artifacts/ms2linux/bin/MineSweeper.App.Linux/release && DOTNET_ROOT=~/.dotnet ./Minesweeper2'
```

> **为什么以前启动不起来**：之前给的命令写在双引号里，PowerShell 会先把 `$HOME` 展开成
> Windows 路径，于是 `cd` 到一个不存在的目录。用单引号或 `run.sh` 就不会。

### 纯 Linux 桌面

```bash
# 装完 .deb 之后
minesweeper2

# 或者不安装，直接跑
~/artifacts/ms2linux/deb/publish/Minesweeper2
```

### 无头截图（不需要显示器，可进 CI）

```bash
scripts/run.sh --screenshot /tmp/shots
```

会输出 15 张 PNG：本版新增、初始、首点、插旗、高级、设置、关卡、商店、成就、记录、关于、瞄准、百万格、深色、英文。


## 5. 版本号与发布约定（Linux 端独立节奏）

Linux 端与 Windows 端共用同一个 Core，但**发布节奏互不干扰**：

| 端 | tag 格式 | 游戏内显示 |
|---|---|---|
| Windows | `v1.5.2` | `v1.5.2` |
| **Linux** | **`linux-v1.5.2`** | **`v1.5.2-Linux`** |

- 游戏里的版本号一律带 `-Linux` 后缀（`UpdateService.DisplayVersion`），方便玩家报版本时分清平台。
- 「检查更新」只认 **`linux-v*`** 前缀的 release（`UpdateService.ReleaseTagPrefix`），
  Windows 的 release 不会被误判成"Linux 有新版"。
- 没有 Linux release 时，设置面板会显示「暂无 Linux 版发布」，而不是"检查失败"。
- CI（`.github/workflows/linux.yml`）在 `linux-v*` tag 上构建并打包 `.deb`。

发一个 Linux 版本：

```bash
git tag linux-v1.5.2
git push origin linux-v1.5.2
```

## 6. 打 `.deb`

```bash
packaging/pack-deb.sh            # 默认版本 1.5.2
# 产物：~/artifacts/ms2linux/deb/minesweeper2_1.5.2_amd64.deb
```

安装：

```bash
sudo apt install ./minesweeper2_1.5.2_amd64.deb
# 或
sudo dpkg -i minesweeper2_1.5.2_amd64.deb && sudo apt -f install
```

卸载：`sudo apt remove minesweeper2`

### 包结构

```
/usr/lib/minesweeper2/          # self-contained 运行时 + 程序（约 104 MB 解压）
/usr/bin/minesweeper2           # → ../lib/minesweeper2/Minesweeper2
/usr/share/applications/minesweeper2.desktop
/usr/share/icons/hicolor/{256x256,512x512}/apps/minesweeper2.png
/usr/share/metainfo/io.github.fastrof.minesweeper2.metainfo.xml
/usr/share/doc/minesweeper2/{copyright,changelog.gz}
/usr/share/lintian/overrides/minesweeper2
```

包体积：**约 35 MB**（压缩后），解压约 104 MB。
依赖：`libc6, libstdc++6, libgcc-s1, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libfreetype6, fonts-dejavu-core`。

---

## 7. 当前实现范围（Spike）

| 功能 | 状态 |
|---|---|
| 经典规则（左键翻开 / 右键三态 / 首点安全 / 计时 / 剩余雷数 / 踩雷摊开） | ✅ 走 Core |
| 三档难度（9×9/10、16×16/40、30×16/99） | ✅ |
| 自绘棋盘 + 立体边 + 数字配色（1 蓝 2 绿 3 红…） | ✅ |
| **远景行程合并 + 连续行色带合并** | ✅ 实测 1000×1000 视口 2.6 万格 → **2 次绘制 / 0.31 ms** |
| 无头截图（CI 视觉回归） | ✅ 6 张 |
| 滚轮缩放 / 拖动平移 | ⬜ 待补（`BoardViewport` 已就位） |
| 双击 / 中键 chord | ⬜ 待补 |
| 36 关关卡表 | ⬜ 待补（Core 已就绪） |
| 道具 / 金币商店 / 成就 / 记录 / 历史 | ⬜ 待补（Core 已就绪） |
| 主题 / 双语 | ⬜ 待补（Core `BoardPalette` / `StringTables` 已就绪） |
| 存档（XDG 路径） | ⬜ 待补 |
| 通知（D-Bus）/ 音效 | ⬜ 待补 |
| 无障碍（AT-SPI2） | ⬜ 待补 |

## 8. 与 Windows 端的已知差异

1. **默认浅色界面**：Windows 端默认深色窗口，Linux 端**默认浅色**——因为 WSLg / 部分 Linux 桌面的
   "系统主题"探测不可靠，会把界面判成全黑。"跟随系统"在 Linux 端按浅色处理，玩家可在设置里显式选「深色」。
   界面配色统一走 `Services/UiPalette.cs`（浅/深两套），面板不再各自硬编码颜色。
2. **字体**：棋盘数字用 `DejaVu Sans`（Linux 自带），Windows 用 Consolas。
   `FormattedText` 在 Avalonia 12 已过时，且 headless 下字体解析不可用，故改用 `TextLayout`。
3. **DPI/窗口尺寸**：Windows 端按"26 DIP/格 + 装饰 150×380"算窗口；这里先简单自适应（待重新标定）。
4. **代码量**：Windows `MainWindow.xaml.cs` 5,070 行（含约 1,400 行 `SAOLEI_*` 自检）；
   Linux 端刻意保持薄：窗口约 390 行、棋盘控件约 780 行、面板各自独立。
5. **`InvariantGlobalization=true`**：去掉 ICU 依赖，顺带修掉 `"F1"` 在非中文 locale 下输出 `12,3` 的问题。
6. **`.cmd` 提示全英文**：见 §4 的编码说明。


## 9. 目录

```
MineSweeper2.0-Windows/linux/
├── global.json / Directory.Build.props / Directory.Packages.props
├── src/MineSweeper.App.Linux/
│   ├── Program.cs               启动 + --screenshot 分发
│   ├── App.axaml(.cs)           FluentTheme
│   ├── Screenshots.cs           无头渲染截图
│   ├── Controls/BoardControl.cs 自绘棋盘（含远景合并）
│   ├── Rendering/BoardViewport.cs  视口换算（移植自 Windows）
│   ├── Rendering/LinuxBoardTheme.cs 配色
│   └── Views/MainWindow.axaml(.cs)
├── scripts/build.sh
└── packaging/
    ├── pack-deb.sh
    ├── minesweeper2.desktop
    └── minesweeper2.metainfo.xml
```
