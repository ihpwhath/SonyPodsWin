<div align="center">

# 🎧 SonyPodsWin

**专为索尼 WH-1000XM6 打造的原生 Windows 11 桌面控制中心**  
基于 **WinUI 3 (.NET 8)** 构建 · Mica 沉浸式深色界面 · 绿色自包含单文件 · 免安装即开即用

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D4?logo=windows)](https://github.com/ihpwhath/SonyPodsWin/releases)
[![Framework](https://img.shields.io/badge/UI-WinUI%203%20%2F%20.NET%208-512BD4?logo=dotnet)](https://github.com/ihpwhath/SonyPodsWin)
[![Release](https://img.shields.io/badge/Release-v0.1.1-D4B16A)](https://github.com/ihpwhath/SonyPodsWin/releases/tag/v0.1.1)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE_SONYPODS_GPL3.txt)

[**⬇️ 下载最新版 (Releases)**](https://github.com/ihpwhath/SonyPodsWin/releases/latest) · [**📦 GitHub Actions 构建产物**](https://github.com/ihpwhath/SonyPodsWin/actions)

</div>

---

## ✨ 核心特性

### 1. 🔇 降噪与音频控制 (`降噪与音质`)
- **实时设备状态**：精准显示耳机型号、实时电量环形进度条、当前蓝牙音频编码（`LDAC` / `AAC` / `SBC` / `LC3`）与固件版本。
- **三段式降噪切换**：一键切换 **降噪 (ANC)** / **环境声 (ASM)** / **关闭**，连接时自动探测并同步耳机当前真实模式。
- **精细化环境声调节**：当且仅当处于「环境声」模式时自动展开控制卡片，支持 **1–20 级环境声强度滑块**、**关注语音（Voice Focus）** 与 **自适应环境声灵敏度**。
- **10 段图形均衡器 (10-Band EQ)**：
  - 内置索尼官方预设（关闭 / 重低音 / 清澈 / 硬朗 / 柔和 / 游戏 / 自定义 / 自定 1 / 自定 2）。
  - 支持直接拖拽 **31Hz – 16kHz** 10 段频响曲线节点，松手即刻同步至耳机 DSP。
- **音质增强开关**：支持 **DSEE 音质提升** 与 **声音质量优先（高码率 LDAC 优先 / 连接稳定性优先）** 切换。

### 2. 🧠 索尼智能控制 (`索尼智能控制`)
- **智能免摘 (Speak-to-Chat)**：开口说话自动暂停音乐并切入环境声。
- **多点连接 (Multipoint)**：管理双设备同时连接开关，并实时展示当前已配对/已连接的蓝牙设备列表。
- **影院模式 (Cinema Upmix)**：空间声场环绕增强开关。
- **耳机音量与媒体控制**：直接读写耳机端原生音量（`0–100%`），内置 **上一曲 / 播放 / 暂停 / 下一曲** 快捷指令与 **远程关闭耳机电源**。

### 3. 💻 Windows 系统联动 (`Windows 联动增强`)
- **开机自动启动（静默驻留托盘）**：一键写入当前用户启动项，随 Windows 登录静默驻留右下角金色 `S` 托盘图标，不弹窗打扰。
- **不抢占手机蓝牙**：软件启动与后台驻留期间**绝不强制自动抢连耳机**，仅在您点击「连接」时建立控制通道，彻底避免“想用手机听歌却被电脑强行抢走连接”的痛点。
- **Windows 原生通知中心接入**：连接成功及低电量预警均通过 Windows 原生 Toast 系统通知推送；右键托盘图标可随时盲切「降噪 / 环境声 / 关闭」。
- **媒体自动化**：支持连接成功自动播放、断开连接自动暂停、连接后自动设定默认音量、以及自定义低电量提醒阈值（`5% – 50%`）。

### 4. 🛠️ 隐藏式开发者协议日志
- 默认隐藏且**后台零性能开销**（CPU 占用 `0%`）。
- 如需抓包调试，前往 **「关于」** 页面**连续点击 5 次版本号 (`版本 0.1.1`)** 即可解锁「协议日志」页面，支持按级别（`INFO` / `FRAME` / `ERROR`）与关键字实时过滤、一键复制，调试完毕点击右上角 **「停止并隐藏」** 即可关闭。

---

## 🚀 下载与使用

1. 前往 [**Releases 页面**](https://github.com/ihpwhath/SonyPodsWin/releases/latest) 下载最新的 `SonyPodsWin-v0.1.1-win-x64.zip`。
   - 国内网络无法访问 GitHub 时，可使用镜像下载最新版：[SonyPodsWin-v0.1.1-win-x64.zip](https://hdsky.us.ci/dl-s7pw3x/SonyPodsWin-v0.1.1-win-x64.zip)（v0.1.0 历史存档：[SonyPodsWin-v0.1.0-win-x64.zip](https://hdsky.us.ci/dl-s7pw3x/SonyPodsWin-v0.1.0-win-x64.zip)）
2. 解压到任意固定文件夹（如 `D:\Apps\SonyPodsWin`）。
3. 先将 **WH-1000XM6** 与 Windows 系统蓝牙配对并连接音频。
4. 双击运行 **`SonyPodsWin.exe`**，点击右上角 **「连接」** 即可开始控制。
   - *提示：关闭主窗口后程序会驻留在系统托盘；如需开机自启，可在「Windows 联动增强」中打开「开机自动启动」。*

---

## 🛠️ 从源码构建

本项目采用 **WinUI 3 (Windows App SDK 1.6)** + **.NET 8** 构建，支持非打包（Unpackaged）自包含单文件发布：

```powershell
# 1. 克隆仓库
git clone https://github.com/ihpwhath/SonyPodsWin.git
cd SonyPodsWin

# 2. 还原依赖（需指定 win-x64 RID）
dotnet restore "winui/SonyPodsWin/SonyPodsWin.csproj" -r win-x64

# 3. 发布自包含单文件可执行程序
dotnet publish "winui/SonyPodsWin/SonyPodsWin.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:WindowsPackageType=None `
  -o "publish/win-x64"
```

---

## ☕ 支持与赞赏

软件全部功能已完整免费开放。如果它帮到了您，欢迎扫码请作者喝杯咖啡，打赏纯属自愿（购买及售后问题可通过小红书联系）：

<div align="center">
  <img src="https://hdsky.us.ci/zanshangma.jpg" alt="赞赏码" width="240" />
</div>

---

## 📄 致谢与许可证

- 索尼 Tandem V2 蓝牙协议参考开源项目 [SonyPods](https://github.com/Mercury000/SonyPods)（作者 Mercury000）实现，并针对 **WH-1000XM6** 实机协议特征（如 `0x09` 长格式电量通知、`0x19` 自适应降噪指令集与 10 段 EQ 扩展帧）进行了专门适配与修正。
- 第三方开源声明详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，许可证全文详见 [LICENSE_SONYPODS_GPL3.txt](LICENSE_SONYPODS_GPL3.txt)（GPL-3.0）。
