# SonyPodsWin 第三阶段交付说明（托盘、电量弹窗、设置持久化与编译自检）

## 1. 本阶段新增与改动文件（均位于 `winui/SonyPodsWin/` 下）

| 仓库相对路径 | 类型 | 说明 |
|---|---|---|
| `winui/SonyPodsWin/Services/SettingsService.cs` | **新增** | `~/.sonypodswin.json` 读写（8 个键、UTF-8 无转义、默认值、容错与 `low_battery_threshold` clamp 5..50） |
| `winui/SonyPodsWin/Services/TrayIconService.cs` | **新增** | 原生 Win32 `Shell_NotifyIconW` 托盘图标、5 项右键菜单、动态 tooltip、低电量 Warning 通知气泡与 64×64 金色 `S` 图标生成 |
| `winui/SonyPodsWin/Views/BatteryPopupWindow.xaml` + `.xaml.cs` | **新增** | 350×182 无边框置顶电量悬浮弹窗（屏幕右下 14px、5s 自动隐藏、悬停停表/离开 3s 隐藏、≤20% 红色徽标、按钮序【降噪/关闭/环境声】） |
| `winui/SonyPodsWin/App.xaml.cs` | **更新** | 串联配置加载/保存、托盘生命周期、弹窗唤起、低电量提醒、启动并行扫描与自动连接 `last_address`、退出清理 |
| `winui/SonyPodsWin/MainWindow.xaml.cs` | **更新** | 接入 `AppWindow.Closing` 关窗隐藏到托盘、`RestoreAndActivate()`、`SetWindowIcon(hIcon)`、`CloseForExit()` |

- **红线核对**：`Protocol/*`、`Services/BleService.cs`、`SonyPodsWin.csproj` 及第二阶段五页 `Views/*`、`Controls/EqualizerControl.cs`、`Themes/*`、`Assets/ZanshangAsset.cs` **全部零改动**。

## 2. 技术选型与新增 NuGet 包引用

- **托盘实现选型**：采用**原生 Win32 `Shell_NotifyIconW` + `CreatePopupMenu` + GDI 32 位 DIB 图标**（`Services/TrayIconService.cs`），未引入 `H.NotifyIcon.WinUI`。
  - **原因**：`SonyPodsWin.csproj` 使用最新稳定版 `Microsoft.WindowsAppSDK 2.7.0` 且默认开启 `PublishSingleFile=true`；原生 P/Invoke 不存在第三方包与新 SDK 版本冲突或单文件提取路径问题。
- **低电量通知选型**：采用 `Shell_NotifyIconW` 的 `NIF_INFO | NIIF_WARNING` 系统托盘气泡通知（Windows 10/11 会以原生通知横幅进入通知中心），与 Python 版 `QSystemTrayIcon.showMessage(..., Warning, 5000)` 底层 Win32 调用完全一致，且在 `WindowsPackageType=None` 非打包单文件下无需额外注册 AUMID / 开始菜单快捷方式即可弹出。
- **新增 NuGet 包引用**：**无（0 个新包）**，`SonyPodsWin.csproj` 无需任何修改。

## 3. `parity-checklist.md` §0 / §3 / §4 逐项落地核对表

| 对照表条目 | 落地实现 | 核对结果 |
|---|---|---|
| §0 关窗隐藏到托盘不退出 | `MainWindow.OnAppWindowClosing`：托盘可见且非退出态时 `args.Cancel = true; AppWindow.Hide();` | 已落地 |
| §3 `~/.sonypodswin.json` 路径与格式 | `UserProfile/.sonypodswin.json`，UTF-8、`WriteIndented=true`、`UnsafeRelaxedJsonEscaping` | 已落地 |
| §3 8 个键与默认值 | `last_name`("WH-1000XM6")、`last_address`(null)、`show_popup_on_connect`(true)、`auto_reconnect`(true)、`auto_play`(false)、`auto_pause`(true)、`default_volume`(54)、`low_battery_threshold`(30) | 已落地 |
| §3 读取容错与 clamp 规则 | 缺文件/异常静默忽略；`default_volume` `int` 直用不 clamp；`low_battery_threshold` clamp 到 `5..50` | 已落地 |
| §3 写入时机与耳机侧不落盘 | 仅响应 `AppStateViewModel.SettingsChanged`（连接成功、型号更新、页 2 控件变更）；降噪/EQ/DSEE/多点不入 JSON | 已落地 |
| §3 启动加载后并行扫描与自动连接 | `App.OnLaunched`：先 `Load`+`ApplySettings`，再并行 `ScanDevicesAsync()` 与（若 `CurrentAddress` 非空）`ConnectToCurrentAsync()` | 已落地 |
| §4.1 64×64 金色圆角 `S` 图标 | `TrayIconService.CreateGoldSIcon()`（#D4B16A 半径 14 + 深色粗体 S），同时复用于主窗口 `WM_SETICON` | 已落地 |
| §4.1 托盘 5 项菜单与顺序 | `打开主面板` / 分隔 / `切换：降噪模式` / `切换：环境声模式` / `切换：关闭降噪` / 分隔 / `退出程序` | 已落地 |
| §4.1 单击弹电量卡 / 双击开主窗 / tooltip | `WM_LBUTTONUP` 弹弹窗、`WM_LBUTTONDBLCLK` 唤起主窗、电量回包更新 `{型号名} · 电量 {pct}%` | 已落地 |
| §4.1 退出清理 | `App.QuitApplicationAsync()`：关弹窗、断开 BLE（最多等 2s）、销毁托盘、关主窗、`Exit()` | 已落地 |
| §4.2 BatteryPopup 尺寸/定位/自动隐藏 | 350×182、无边框置顶不入任务栏、主屏工作区右下角 14px、5s 自动藏、悬停停表/离开 3s 藏 | 已落地 |
| §4.2 电量徽标与红金配色 | 24×12 轮廓 + 最小 5% 填充条、`pct <= 20` 用 `#EF4444` 红、其余 `#D4B16A` 金、`--%` 与 ` · {codec}` 拼串 | 已落地 |
| §4.2 弹窗 NC 快捷按钮顺序 | 严格按对照表原样：**降噪 / 关闭 / 环境声**（未与主页统一） | 已落地 |
| §2 / §4 低电量提醒 | 接 `LowBatteryAlertRequested`（阈值 5–50 默认 30、每连接会话仅触发一次），标题 `SonyPods 低电量提醒`，正文 `{型号名} 电量仅剩 {pct}%，请及时充电。` | 已落地 |

## 4. 拿不准或需在 Windows 实机确认的点

1. **托盘左键双击时先触发一次单击**：Win32 托盘消息流中，双击通常会先收到 `WM_LBUTTONUP` 再收到 `WM_LBUTTONDBLCLK`，因此双击托盘时可能会先闪一下电量弹窗再唤起主窗口（Qt 侧也是同样机制）。如希望双击完全不闪弹窗，可后续加一个约 200ms 的单击延迟定时器，在收到双击时取消单击回调。
2. **WinUI 3 无边框窗口圆角与真半透明**：`BatteryPopupWindow` 使用 `OverlappedPresenter.SetBorderAndTitleBar(false, false)` + `#F82A2D32` 背景 + 12px 圆角 `Border`。在 Windows 11 上系统默认提供窗口圆角，但 WinUI 3 `Window` 宿主表面默认不透明（不像 Qt `WA_TranslucentBackground`），四个圆角外极细像素可能露出深色宿主底色；若视觉上介意，可在实机微调 `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND)` 或移除外圆角差。
3. **高 DPI 缩放下 350×182 物理像素 vs 逻辑像素**：`AppWindow.Resize(new SizeInt32(350, 182))` 接受物理像素；在 150% / 200% 缩放的 4K 屏上会比 100% DPI 下显得偏小，需在实机上乘以 `XamlRoot.RasterizationScale`（或 `GetDpiForWindow / 96.0`）换算后再调用 `Resize` 与右下角 14px 偏移。

## 5. Windows 上首次编译自检清单

### 推荐命令（在仓库根目录执行）

```powershell
# 1. Debug 编译检查（最快暴露 XAML / C# 编译错误）
dotnet build winui/SonyPodsWin/SonyPodsWin.csproj -c Debug -r win-x64

# 2. Release 非打包自包含单文件发布
dotnet publish winui/SonyPodsWin/SonyPodsWin.csproj -c Release -r win-x64
```

### 最可能报错的 3 个点与排查方案

1. **NuGet 还原或 SDK 版本不匹配（`NU1102` / `WindowsAppSDK 2.7.0` 或 `BuildTools 10.0.28000.2957` 找不到或与本机 VS/SDK 不兼容）**
   - **现象**：`dotnet build` 在 restore 阶段报错找不到对应包版本，或提示需要更高版本 .NET SDK / MSBuild。
   - **处理**：先确认本机已安装 .NET 8 SDK（x64）与官方 `nuget.org` 源；如本机仅装了旧版 Visual Studio 工具链，可将 `SonyPodsWin.csproj` 中 `Microsoft.WindowsAppSDK` 与 `Microsoft.Windows.SDK.BuildTools` 对齐到本机可还原的最新稳定版（例如 1.8.x / 2.x 稳定版）。
2. **`PublishSingleFile=true` + `EnableMsixTooling=true` 在构建/发布阶段触发 WinUI 资产或 PRI 打包校验错误（如 `PRI175` / `MSB4018` / 单文件启动 `0xc000027b`）**
   - **现象**：`Debug` 构建通过，但 `dotnet publish` 失败，或单 exe 双击无界面闪退。
   - **处理**：确认 `PublishTrimmed` 保持 `false`、`IncludeAllContentForSelfExtract` 与 `IncludeNativeLibrariesForSelfExtract` 均为 `true`；若特定 Windows App SDK 2.x 版本出现发布回归，可先用 `-p:PublishSingleFile=false` 验证目录自包含版能否正常启动以隔离定位。
3. **XAML 编译器对 `ProtocolLogPage.xaml` 的 `x:Bind` 或 `ms-appx:///Themes/*.xaml` 路径报错（`WMC0001` / `XLS0414`）**
   - **现象**：提示找不到 `Themes/Colors.xaml` 资源或 `DataTemplate` 中 `x:Bind FormattedLine` 绑定失败。
   - **处理**：确认解压后的文件夹结构严格保持 `winui/SonyPodsWin/Themes/Colors.xaml`（与 `App.xaml` 中 `ms-appx:///Themes/...` 一致），且 `SonyPodsWin.csproj` 在 `winui/SonyPodsWin/` 根目录，以便 SDK 默认通配符自动把 `Themes/*.xaml`、`Views/*.xaml` 纳入 `Page` 项组。
