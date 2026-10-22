# Linux Spike 交付报告

> 日期：2026-10-21 ｜ 基线：Windows 仓库 v1.5（`043c577`）
> 目标：验证"在 v1.5 基础上做 Linux 版并把 `.deb` 打出来"这条路能走通

---

## 1. 结论

**走通了。** 从零到"能玩 + 能出包"用了约 1 小时，且 **Windows 仓库零改动**。

| 验证项 | 结果 |
|---|---|
| Avalonia 12.1.4 + .NET 10 在 Ubuntu 26.04 上编译 | ✅ **0 警告 0 错误** |
| 自绘棋盘 + Core 规则联动 | ✅ 可玩（左键翻开 / 右键三态 / 首点安全 / 数字 / 旗 / 计时 / 剩余雷数） |
| 远景行程合并（移植 Windows 算法） | ✅ 1000×1000 视口 **2.6 万格 → 2 次绘制 → 0.31 ms** |
| 无头渲染截图（CI 可用） | ✅ 6 张 PNG |
| `.deb` 打包 | ✅ **35 MB**（解压 104 MB），`dpkg-deb` 通过 |
| 解包运行（不安装） | ✅ 通过 `/usr/bin/minesweeper2` 启动，6 张截图全部生成 |
| `lintian` | ✅ **零告警**（E/W 全清，含 override） |
| `appstreamcli validate` | ✅ 通过 |
| **Windows 仓库是否被改动** | ✅ **`git status` / `git diff` 为空，HEAD 未变** |

---

## 2. 隔离机制（为什么 Windows 端不会被碰）

| 机制 | 实现 |
|---|---|
| Core 复用 | Linux 工程用 `<Compile Include="..\..\..\MineSweeper2.0-Windows\src\MineSweeper.Core\**\*.cs" LinkBase="LinkedCore" />` **链接编译**，不复制、不修改、不需要 ProjectReference |
| 构建产物 | `scripts/build.sh` 固定 `--artifacts-path "$HOME/artifacts/ms2linux"`，`obj/bin` 全在 Linux 侧 |
| 打包产物 | `pack-deb.sh` 用 `dotnet publish -o <WSL 路径>`，最终 `.deb` 也在 Linux 侧，最后只把 `.deb` 复制回来 |
| 图标 | 从 Windows 仓库 `docs/assets/img/icon-{256,512}.png` **只读复制** |
| 验证 | 收尾时 `git status --short` 与 `git diff --stat` 均为空；Windows 源文件最后修改时间 ≤ 11:13:28（早于本次 11:44 的首次写操作） |

> 代价：如果 Core 将来新增 NuGet 包依赖，需要在 Linux 工程的 csproj 里同步一份。
> 目前 Core 零包依赖，所以这条代价暂时为零。

---

## 3. 交付物

```
MineSweeper2.0-Windows/linux/
├── global.json                      SDK 锁定 10.0.402
├── Directory.Build.props            Linux 端编译约定（InvariantGlobalization=true）
├── Directory.Packages.props         中央包管理（Avalonia 12.1.4）
├── README.md                        构建/运行/打包说明
├── src/MineSweeper.App.Linux/       Avalonia 工程（14 个文件，约 1,100 行）
│   ├── Program.cs                   启动 + --screenshot 分发
│   ├── Screenshots.cs               无头渲染（6 个场景）
│   ├── Controls/BoardControl.cs     自绘棋盘 + 远景合并（约 480 行）
│   ├── Rendering/BoardViewport.cs   视口换算（移植自 Windows）
│   ├── Rendering/LinuxBoardTheme.cs 经典配色
│   └── Views/MainWindow.axaml(.cs)  状态栏 + 难度 + 诊断行
├── scripts/build.sh
├── packaging/{pack-deb.sh, *.desktop, *.metainfo.xml}
└── dist/minesweeper2_1.5.2_amd64.deb   ← 35.9 MB
```

---

## 4. 实测数据

### 4.1 构建

```text
dotnet build -c Release --artifacts-path ~/artifacts/ms2linux
→ 0 个警告 / 0 个错误 / 2.8 s
```

### 4.2 渲染（Avalonia → Skia，headless 真实光栅化）

| 场景 | 可见格数 | 绘制调用 | 耗时 |
|---|---:|---:|---:|
| 初级 9×9（正常缩放） | 81 | 266 | 9.37 ms（首帧含布局） |
| 高级 30×16（正常缩放） | 480 | 2,401 | 0.71 ms |
| **1000×1000 远景** | **25,976** | **2** | **0.31 ms** |

> 远景那条是关键：**2.6 万个可见格被"同色行程 + 连续行色带"合并成 2 次绘制调用**，
> 与 Windows 端 `BoardRenderer` 的算法逐行等价（只是把 `CanvasDrawingSession` 换成 `DrawingContext`）。

### 4.3 `.deb`

```text
体积        : 35 MB（解压 104 MB，226 个条目）
Architecture: amd64
Depends     : libc6 (>=2.35) libstdc++6 libgcc-s1 zlib1g libx11-6 libice6
              libsm6 libfontconfig1 libfreetype6 fonts-dejavu-core
lintian     : 零告警
appstream   : 校验通过
SHA256      : F9EE96C1C1AB51F8BDDC83BBD15D59B2C0B346B901E29A9E561FE07713DB509A
```

---

## 5. 踩到的坑（都已解决，写下来免得重踩）

| # | 现象 | 根因 | 修法 |
|---|---|---|---|
| 1 | `Could not create glyphTypeface. Font family: Consolas` | Windows 字体在 Linux 不存在；Avalonia 复合字体在 headless 下不自动回退 | 改用系统自带 `DejaVu Sans`；`.deb` 声明 `fonts-dejavu-core` |
| 2 | `FormattedText` 在 headless 下 `get_Width()` 抛异常 | Avalonia 12 已把 `FormattedText` 标记过时，其字体路径与 TextBlock 不同 | 改用 `TextLayout` + `layout.Draw(context, point)` |
| 3 | `dotnet --version` 报 `Failed to create CoreCLR 0x80070057` | SDK 装在 `/mnt/d`（DrvFs） | 装到 `~/.dotnet`、源码放 ext4 |
| 4 | MSBuild 报 `XML comment cannot contain '--'` | csproj 注释里写了 `--artifacts-path` | 注释里避免 `--`（两次踩到） |
| 5 | lintian `dir-or-file-in-opt` | 首次装到 `/opt` | 改 `/usr/lib/minesweeper2`（已实测规范） |
| 6 | lintian `executable-not-elf-or-script [.dll]` / `shared-library-is-executable` | `cp -a` 从 DrvFs 带入了 777/755 | 打包时统一 `chmod 644` 托管文件、`chmod 755` 可执行 |
| 7 | lintian `icon-size-and-directory-name-mismatch 256x252` | 用了 `docs/images/icon-256.png`（非正方形） | 改用 `docs/assets/img/icon-{256,512}.png`（正方形） |
| 8 | WSLg 的 `DISPLAY` 为空、`/tmp/.X11-unix` 无 socket | 这台机器的 WSLg GUI 通道未启用 | 用 Avalonia Headless + Skia 做截图（不依赖显示服务器，反而更适合 CI） |

---

## 6. 当前能玩 / 还不能玩

**能玩**：经典规则全部（规则在 Core）、三档难度、自绘棋盘、首点安全、计时、剩余雷数、踩雷摊开、远景合并。

**还没接**（Core 都已就绪，只差 UI）：
滚轮缩放/拖动平移、双击与中键 chord、36 关关卡表、道具/金币商店/成就/记录/历史、主题/双语、存档（XDG）、通知（D-Bus）、音效、无障碍（AT-SPI2）。

---

## 7. 下一步建议（按顺序）

| 顺序 | 事项 | 理由 |
|---|---|---|
| 1 | **在真实 Linux 桌面/真机上跑一次** | 本次 WSLg 不可用，真实窗口、DPI、输入手感都没验证过 |
| 2 | 补**缩放/平移 + chord**（约 3–5 人日） | 这三样决定"像不像扫雷"，`BoardViewport` 已就位，逻辑可搬 Windows 的 400 行 |
| 3 | 接**存档（XDG）+ 36 关关卡表**（约 3–5 人日） | 有了存档和关卡才叫"能玩下去" |
| 4 | 把截图模式接进 **GitHub Actions**（半天） | 每次提交自动出 6 张图 + `lintian` + `dpkg-deb`，防止视觉回归 |
| 5 | 再谈**功能对齐**（道具/成就/双语…，约 40–60 人日） | 到这一步才是"1.5 功能对齐版" |

---

## 8. 复现命令

```bash
# 构建
wsl -d Ubuntu -- bash /mnt/d/.../MineSweeper2.0-Windows/linux/scripts/build.sh

# 无头截图
wsl -d Ubuntu -- bash -lc 'cd ~/artifacts/ms2linux/bin/MineSweeper.App.Linux/release && \
  DOTNET_ROOT=$HOME/.dotnet ~/.dotnet/dotnet Minesweeper2.dll --screenshot /tmp/shots'

# 打 deb
wsl -d Ubuntu -- bash /mnt/d/.../MineSweeper2.0-Windows/linux/packaging/pack-deb.sh
```
