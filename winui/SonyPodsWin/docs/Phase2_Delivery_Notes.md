# SonyPodsWin 第二阶段交付说明（界面与业务编排）

## 1. 仓库文件清单（均位于 `winui/SonyPodsWin/` 下）

| 仓库相对路径 | 说明 | 状态 | 
| ----- | ----- | ----- | 
| `winui/SonyPodsWin/SonyPodsWin.csproj` | 非打包自包含单文件工程（第一阶段定稿） | 保持不变 | 
| `winui/SonyPodsWin/Protocol/Constants.cs`、`Framing.cs`、`Table1.cs`、`Table2.cs` | 协议层地基（第一阶段定稿） | 保持不变 | 
| `winui/SonyPodsWin/Services/BleService.cs` | WinRT BLE 传输服务（第一阶段定稿） | 保持不变 | 
| `winui/SonyPodsWin/Themes/Colors.xaml` | 深色 Fluent 色板与规格资源键 | **本阶段新增** | 
| `winui/SonyPodsWin/Themes/Brushes.xaml` | 自定义画刷资源（不覆盖系统 Fluent 键） | **本阶段新增** | 
| `winui/SonyPodsWin/Themes/Styles.xaml` | 卡片 8px 圆角/1px 边框、按钮、排版与等宽日志样式 | **本阶段新增** | 
| `winui/SonyPodsWin/App.xaml` + `App.xaml.cs` | 启用深色主题、合并 Themes、创建 `BleService` 与 `AppStateViewModel` | **本阶段更新** | 
| `winui/SonyPodsWin/MainWindow.xaml` + `MainWindow.xaml.cs` | `NavigationView`（无设置项）+ `Frame` 五页骨架、`MicaBackdrop`、980×640 默认 / 900×600 最小、标题 `SonyPods` | **本阶段更新** | 
| `winui/SonyPodsWin/ViewModels/AppStateViewModel.cs` | 全局状态、握手 18+4 条查询序列、响应分发、协议协商、第三阶段预留接口 | **本阶段新增** | 
| `winui/SonyPodsWin/Controls/EqualizerControl.cs` | 10 段可拖 EQ 曲线（Catmull-Rom 细线无渐变、选中香槟金描边、动态 10/5 段协商） | **本阶段新增** | 
| `winui/SonyPodsWin/Assets/ZanshangAsset.cs` | `zanshangma_small.jpg` 内嵌 Base64 常量与运行时 `BitmapImage` 解码 | **本阶段新增** | 
| `winui/SonyPodsWin/Views/NoiseAudioPage.xaml(.cs)` | Page 01：固定顶部设备区 + 降噪三选 + 环境声滑杆 + 两开关 + 10 段 EQ（ComboBox 预设）+ DSEE + 音质优先 | **本阶段新增** | 
| `winui/SonyPodsWin/Views/SonySmartPage.xaml(.cs)` | Page 02：两列 1:1 智能聆听/多点/影院/配对表/自动关机 + 音量/播放控制/关机确认 | **本阶段新增** | 
| `winui/SonyPodsWin/Views/WindowsIntegrationPage.xaml(.cs)` | Page 03：六项纵向联动设置卡片（默认值与范围逐项对齐） | **本阶段新增** | 
| `winui/SonyPodsWin/Views/ProtocolLogPage.xaml(.cs)` | Page 04：实时滚动协议日志（级别筛选、搜索、复制、清空、Cascadia Mono 12px） | **本阶段新增** | 
| `winui/SonyPodsWin/Views/AboutPage.xaml(.cs)` | Page 05：图标、名称、版本、简介、GitHub 链接、默认折叠赞赏 Expander | **本阶段新增** | 

## 2. `parity-checklist.md` 逐项落地核对表

| 对照表章节 / 功能项 | 落地阶段 | 落地情况与说明 | 
| ----- | ----- | ----- | 
| §0 窗口尺寸/Mica/Dark/NavigationView/标题 `SonyPods` | **第二阶段（已完成）** | 默认 980×640、最小 900×600（`OverlappedPresenter`）、`MicaBackdrop`、Dark 主题、`IsSettingsVisible=False` | 
| §0 关窗隐藏到托盘（不退出） | **留到第三阶段** | 待第三阶段托盘图标就绪后接管 `AppWindow.Closing` | 
| §1.1 降噪与音质页全部控件（设备头、降噪三选、环境声 1–20、关注语音、自适应、EQ ComboBox 预设、10 段曲线、DSEE、声音质量优先） | **第二阶段（已完成）** | EQ 预设按规格用原生 `ComboBox`（压过效果图 chips）；松手下发、回包静默同步、支持能力协商 | 
| §1.2 索尼智能控制页全部控件（免摘、多点+slot 协商、影院、配对设备表、自动关机只读、音量 0–100、播放控制 4 键、关机确认弹窗） | **第二阶段（已完成）** | 影院开关按原版无回包同步分支；多点 slot 未知时点击无动作 | 
| §1.3 Windows 联动增强页全部六项（自动播放/暂停、默认音量 54%、连接弹窗、自动重连、低电量阈值 30%） | **第二阶段（已完成）** | 自动播放/暂停已接 `VK_MEDIA_PLAY_PAUSE`；默认音量连接后下发；自动重连实时同步到 `BleService.AutoReconnect`；其余配置变更通过 `SettingsChanged` 事件留口给第三阶段落盘 | 
| §1.4 协议日志页 | **第二阶段（已完成）** | 实时显示 `BleService.LogMessage`、`FrameReceived`（RX）与上层 `TX` 十六进制帧；含级别筛选、搜索、复制、清空 | 
| §1.5 关于页（文案、GitHub、内嵌 Base64 赞赏码 Expander） | **第二阶段（已完成）** | 文案一字不改取自 `parity-checklist.md`；赞赏码由 `ZanshangAsset.cs` 运行时解码，不依赖外部图片文件 | 
| §2 扫描（已配对+广播 3.5s）、设备菜单、握手后 18+4 条查询序列、支持功能协商、Alert 自动回执、低电量阈值判定 | **第二阶段（已完成）** | 低电量判定与 900ms 弹窗延迟均已在 `AppStateViewModel` 编排并抛出 `LowBatteryAlertRequested` / `ConnectPopupRequested` 事件供第三阶段订阅 | 
| §3 设置持久化（`~/.sonypodswin.json` 8 键读写） | **留到第三阶段** | `AppStateViewModel` 已预留 `SettingsSnapshot`、`ExportSettings()`、`ApplySettings(...)`、`SettingsChanged` 事件 | 
| §4 系统托盘图标/菜单/单击弹窗/双击唤起 & `BatteryPopup` 窗口 | **留到第三阶段** | `AppStateViewModel` 已预留 `QuickSetNoiseControl`、`DeviceSummaryUpdated`、`ConnectPopupRequested`、`LowBatteryAlertRequested` | 

## 3. 拿不准或需在 Windows 本地确认的点（逐条列出，不编造）

1. **关于页软件名：`SonyPods` vs `SonyPodsWin`**：`design-winui-v1.md` Page 05 写「名称 TextBlock（SonyPods）」，而 `parity-checklist.md` §1.5 写「软件名：SonyPodsWin」。按本阶段要求「文案一字不改用对照表里的原文」，当前 `AboutPage.xaml` 使用 `SonyPodsWin`（主窗口标题仍为 `SonyPods`）。若希望统一为 `SonyPods`，改一行 XAML 即可。

2. **`Slider` 松手事件（`PointerCaptureLost`）在键盘微调时的触发**：为了避免拖动环境声/音量滑杆时高频轰炸 BLE 发包，当前沿用 Python 的「松手才下发」逻辑，绑定在 `Slider.PointerCaptureLost`；若用户使用键盘方向键调整滑杆，不会触发 `PointerCaptureLost`。如需兼顾键盘微调，可在第三阶段追加 `KeyUp` 或 200ms 防抖。

3. **降噪模式三选一的控件实现**：`design-winui-v1.md` 提到「RadioButtons 水平三选（降噪/环境声/关闭），高 52px、均分宽度；选中背景 #FFD4B16A、选中文字 #FF1E1F22；未选中背景 #FF34373D」。由于 WinUI 3 原生 `RadioButtons` 的默认模板带左侧单选圆圈且不易在深色主题下做成三等分通栏分段块，本实现使用 3 列均分 `Grid` + 3 枚互斥状态 `Button` 实现完全一致的分段视觉与单选行为；若需要严格使用 `RadioButton` 语义（如辅助功能读屏），可替换为自定义 `ControlTemplate` 的 `RadioButton`。

4. **日志页「导出」按钮**：`design-winui-v1.md` 注明「清空/复制/导出 Button（仅原有功能支持时显示）」。对照 `parity-checklist.md` §1.4，原 Python 版日志页只有只读文本框无导出文件功能，本阶段保留了「复制」与「清空」按钮、未额外自创文件导出弹窗。

5. **Windows 本地编译验证**：当前沙盒为 Linux 环境，无法执行 WinUI 3 XAML 编译器（`Microsoft.UI.Xaml.Markup.Compiler`）实测编译。请在 Windows 本地解压后于仓库根目录运行 `dotnet build winui/SonyPodsWin/SonyPodsWin.csproj -c Debug -r win-x64` 验证全部 XAML 与 `x:Bind` 编译通过。