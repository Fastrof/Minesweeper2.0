# 隐私政策 / Privacy Policy

**扫雷 2.0 / MineSweeper 2.0**（以下简称"本应用"）
最后更新 / Last updated：2026 年 10 月 / October 2026

---

## 中文

本应用**不收集、不存储、也不向任何第三方传输任何个人信息**。

1. **无账号**：本应用没有注册、登录或任何身份识别功能，也不知道你是谁。
2. **无遥测、无广告、无第三方 SDK**：不含统计上报、崩溃上报、广告或任何第三方分析组件。
3. **仅在检查更新时联网**：这是本应用**唯一**的网络行为。它通过 HTTPS 请求 GitHub 的公开接口
   （`api.github.com/repos/Fastrof/Minesweeper2.0/releases/latest`）以获取最新版本号。
   该请求**不携带任何可识别你身份的信息**（不含账号、设备号、安装 ID、IP 之外的任何标识），
   也不发送本机的任何数据；返回内容只用于比较版本号。你可以在「设置 / 自定义」里**关闭自动检查更新**，
   关闭后本应用不再发起任何网络请求。Microsoft Store 版本完全不进行这项检查（由商店负责更新）。
4. **数据只存在本机**：游戏设置、关卡进度、道具库存、最快记录、累计统计、成就等，全部保存在本机的应用数据文件夹内
   （未打包版为 `%LOCALAPPDATA%\MineSweeper2\`），不会离开你的设备。删除该文件夹或卸载应用即可彻底清除。
5. **系统通知**：本应用使用 Windows 本地通知（Toast）提示开局、胜利、失败、成就解锁等结果。
   通知内容不含个人信息，也不会上传到任何服务器。若系统关闭了通知，这些提示仍会写入本机的 `app.log`。
6. **不索取敏感权限**：本应用不访问你的位置、通讯录、相机、麦克风、照片或任何其它敏感数据。
   它以完整信任（`runFullTrust`）方式运行，这是 Windows App SDK 桌面应用的框架要求，
   实际用途仅为：创建窗口、在自绘画布上绘制棋盘、读写本机应用数据文件夹、合成并播放本地音效。
7. **未成年人**：本应用不收集任何用户数据，因此也不存在针对未成年人的数据收集。

如对本政策有疑问，请通过以下方式联系：
<https://github.com/Fastrof/Minesweeper2.0/issues>

---

## English

This application **does not collect, store, or transmit any personal information**.

1. **No account**: there is no sign-up, sign-in, or any form of user identification.
2. **No telemetry, ads, or third-party SDKs**: no analytics, no crash reporting, no advertising,
   no third-party components that report data.
3. **Network access happens only when checking for updates.** This is the app's **only** network activity:
   an HTTPS request to GitHub's public API
   (`api.github.com/repos/Fastrof/Minesweeper2.0/releases/latest`) to read the latest version number.
   The request carries **no personally identifying information** (no account, device id, or install id)
   and sends no local data; the response is used solely to compare version numbers.
   You can turn automatic update checks off in *Settings / Customize*, after which the app makes
   no network requests at all. The Microsoft Store build never performs this check
   (the Store handles updates).
4. **All data stays on your device**: settings, level progress, item inventory, best times, statistics and
   achievements are stored in the app's local data folder (for the unpackaged build,
   `%LOCALAPPDATA%\MineSweeper2\`). Deleting that folder or uninstalling the app removes everything.
5. **Notifications**: the app uses local Windows toast notifications for results such as game start,
   victory, defeat and achievement unlocks. Their contents contain no personal information and are never
   uploaded. If notifications are disabled system-wide, the same messages are still written to the local
   `app.log`.
6. **No sensitive permissions**: the app does not access your location, contacts, camera, microphone,
   photos, or any other sensitive data. It runs with full trust (`runFullTrust`), which is required by the
   Windows App SDK for desktop applications; that trust is used only to create the window, draw the board
   on a self-rendered canvas, read and write the app's local data folder, and synthesize/play local audio.
7. **Children**: no user data is collected from anyone, including children.

Contact: <https://github.com/Fastrof/Minesweeper2.0/issues>
