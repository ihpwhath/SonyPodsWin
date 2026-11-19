# SonyPodsWin 第一阶段交付说明

## 1. Python → C# 文件与接口对照表

| Python 源文件 | C# 目标文件（仓库路径 `winui/SonyPodsWin/`） | 对应核心类型 / 接口 | 
| ----- | ----- | ----- | 
| `constants.py` + `__init__.py` | `winui/SonyPodsWin/Protocol/Constants.cs` | `SonyGatt`、`SonyTandemDataType`、`SonySppFrameType`、全部 15 个协议枚举、`FrameRetryPolicy`、`SonyProtocolEnumExtensions`（`AckRequired` / `GetRetryPolicy` / `GetLabel`） | 
| `framing.py` + `__init__.py` | `winui/SonyPodsWin/Protocol/Framing.cs` | `TandemFrame`、`SonyTandemFraming`（`Encode`/`Decode`/`Escape`/`Unescape`/`Checksum`/`InverseSequence`）、`SonySppPayloadMapper`（`OutboundFromTandemBytes`/`InboundToTandemBytes`） | 
| `v2_table1.py` | `winui/SonyPodsWin/Protocol/Table1.cs` | `TandemResponse` 基类、全部 20 个响应 record、`Table1` 全部命令/域常量、34 个 `Build*` 构造器、`Parse()` 总入口 | 
| `v2_table2.py` | `winui/SonyPodsWin/Protocol/Table2.cs` | `PairedDeviceInfo`、`PeripheralDevicesResponse`、`Table2` 常量与 `ParseTable2()` | 
| `ble_transport.py` | `winui/SonyPodsWin/Services/BleService.cs` | `DiscoveredDevice`、`BleConnectionState`、`BleService`（原生 WinRT `BluetoothLEDevice` / `BluetoothLEAdvertisementWatcher` / `GattDeviceService` / `GattCharacteristic`、8 秒断线重连、`ConnectionStateChanged`/`PayloadReceived`/`FrameReceived`/`LogMessage`/`Disconnected` 事件） | 
| （新建工程壳） | `winui/SonyPodsWin/SonyPodsWin.csproj`、`App.xaml(.cs)`、`MainWindow.xaml(.cs)` | `net8.0-windows10.0.19041.0`、`x64`、`UseWinUI=true`、`WindowsPackageType=None`、`EnableMsixTooling=true`、`PublishSingleFile=true`、`Microsoft.WindowsAppSDK 2.7.0` | 

## 2. Python 原版含糊、怪异或与 `protocol-notes.md` 存在差异的点（已全部照搬 Python 并加注释）

 1. **`NC_AMB_TOGGLE`（`0x30`）功能码映射冲突**：`v2_table1.py` 中 `OFF=0x00, NC=0x02, ASM=0x01`，而 `protocol-notes.md` §4.4 记载为 `OFF=0x01, ASM=0x03, NC=0x04`。C# 已严格按 Python 原值保留，后续如涉及 `0x30` 机型需以实机抓包复核。

 2. **SYSTEM 域常量与智能免摘末字节冲突**：`v2_table1.py` 定义 `SYS_SMART_TALKING_TYPE1=0x01`、`TYPE2=0x02`、`WEARING_STATUS_DETECTOR=0x04`，且 `build_set_speak_to_chat_enabled` 末字节写 `0x00`；而 `protocol-notes.md` §4.10 记载为 `0x02` / `0x0C` / `0x06` 且末字节 `0x01`。C# 已按 Python 原值保留。

 3. **`CONNECT_RET_PROTOCOL_INFO` 版本号字节偏移**：`v2_table1.py` 从 `payload[1..4]` 读 4 字节大端版本号；`protocol-notes.md` §2.3 记载 V2 布局 `[0x01][0x00][v3][v2][v1][v0][ena][ena]`（若 `payload` 不含 cmd，则版本号在 `payload[2..5]`）。C# 已按 Python `payload[1..4]` 保留。

 4. **GATT 握手是否订阅 `DETERMINE_MTU`**：`protocol-notes.md` §2.2 提到先订阅 `DETERMINE_MTU` 等 `[0x01]` 再读 `WRITABLE_VALUE_LENGTH`；但已在 XM6 真机验证通过的 `ble_transport.py` 并未订阅 `DETERMINE_MTU`，而是直接读 `WRITABLE_VALUE_LENGTH`（失败回退 200）并订阅 `FROM_ACC` Notify。`BleService.cs` 已按 Python 实机逻辑实现。

 5. **ACK 超时耗尽后的 `seq` 状态**：`ble_transport.py` 中若重试 10 次仍无 ACK，只清空 `_awaiting_ack` 而不翻转 `_next_tx_seq`；C# 已保持同一行为。

 6. **`_pct_or_null` 中的死分支**：Python 写法 `if b == 0xFF or b == 0x00 and False: return None` 实际上 `0x00` 会返回 `0`，仅 `0xFF` 或 `>100` 返回 `None`，C# 等价实现。

 7. **`_len_prefixed_str` 兜底偏移**：当 `length <= 0` 或越界时，Python 无论 `offset` 是多少都从 `payload[1:]` 解码；C# 照搬该兜底。

 8. **`build_set_eq_preset` 的 `PRESET_EQ_AND_ULT_MODE`（`0x03`）**：Python 将 `ultMode` 字节硬编码为 `0x00`，未暴露参数；C# 保持签名与字节一致。

 9. **Table2 缺少命令构造器**：随附 `v2_table2.py` 只有响应解析 `parse_table2()`，不含 Table2 GET/SET 构造器；本阶段未擅自补写未验证的构造器，等第二阶段需要时再按抓包或需求补齐。

10. **8 秒重连策略的细节**：随附的 `ble_transport.py` 本身只暴露 `on_disconnected` 回调，8 秒重连逻辑在未随附的上层控制代码中。`BleService.cs` 按需求内置了「意外断线后每 8 秒重试一次、手动调用 `DisconnectAsync()` 或 `DisposeAsync()` 立即取消重连、可通过 `AutoReconnect` 开关控制」的标准策略。

## 3. WinUI 3 单 exe 可行性如实评估与当前默认配置

* **官方支持状态**：依据 [Microsoft Learn: Distribute an unpackaged WinUI 3 app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)（2026-10 更新），自 Windows App SDK 1.5 起，**同时满足 Unpackaged（`WindowsPackageType=None`）+ Self-Contained（`WindowsAppSDKSelfContained=true` + `SelfContained=true`）** 的 WinUI 3 应用正式支持 `.NET` 的 `PublishSingleFile=true`。

* **默认发布形态（已写入 `winui/SonyPodsWin/SonyPodsWin.csproj`）**：当前工程默认采用**非打包自包含单文件发布**，已齐备以下必选属性组：

  * `UseWinUI=true`、`WindowsPackageType=None`、`EnableMsixTooling=true`（单文件路线下 Windows App SDK 构建校验缺此项会直接报错）

  * `WindowsAppSDKSelfContained=true`、`SelfContained=true`

  * `PublishSingleFile=true`、`IncludeAllContentForSelfExtract=true`、`IncludeNativeLibrariesForSelfExtract=true`

  * `PublishTrimmed=false`（严禁随意开启裁剪）

  默认发布命令（在仓库根目录执行）：
  `dotnet publish winui/SonyPodsWin/SonyPodsWin.csproj -c Release -r win-x64`

* **真实运行机制（自解压单文件）**：生成的单 `.exe` 本质是**自解压可执行包**，首次启动时仍会把 `Microsoft.UI.Xaml.dll`、WinRT 原生 C++ DLL 与 `.pri` 资源解压到 `%TEMP%\.net\SonyPodsWin\...` 目录后再加载；体积通常在 **120–180 MB** 左右。

* **已知风险备忘**：

  1. 必须保持 `PublishTrimmed=false`，否则 WinUI 3 / CsWinRT 在启动时极易触发 `Microsoft.UI.Xaml.dll` `0xc000027b` 闪退（[Microsoft Q&A #5968479](https://learn.microsoft.com/en-us/answers/questions/5968479/winui-3-published-unpackaged-self-contained-app-cr)）；

  2. Windows App SDK 2.x 个别版本存在非打包自包含发布崩溃回归（[WindowsAppSDK#6673](https://github.com/microsoft/WindowsAppSDK/issues/6673)）以及内容资产被打包进单 exe 导致路径解析不同于普通目录发布的差异（[WindowsAppSDK#4242](https://github.com/microsoft/WindowsAppSDK/issues/4242)），若后续第二阶段引入静态资源文件需在实机发布态验证加载路径；如遇 SDK 版本回归，可临时传 `-p:PublishSingleFile=false` 退回目录自包含定位问题。