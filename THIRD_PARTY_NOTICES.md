# 开源协议与参考来源记录 (Third-Party Notices)

本项目在蓝牙协议层（Sony Tandem V2 帧格式、GATT/SPP 路由、命令编码与应答解析）参考并移植了以下开源项目的协议实现：

- **项目名称**：SonyPods
- **作者**：Mercury000 (https://github.com/Mercury000/SonyPods)
- **模块入口**：https://modules.lsposed.org/module/com.mercury.sonypods/
- **原项目许可证**：GNU General Public License v3.0 (GPL-3.0)
- **许可证原文**：见同目录 [LICENSE_SONYPODS_GPL3.txt](LICENSE_SONYPODS_GPL3.txt)

## 参考范围记录
- `src/sonypodswin/protocol/constants.py`：参考 `SonyGatt.kt`、`SonyTandemConstants.kt`、`SonyTandemEnums.kt`、`SonyFunctionType.kt`
- `src/sonypodswin/protocol/framing.py`：参考 `SonyTandemFraming.kt`、`TandemTransportRouting.kt`
- `src/sonypodswin/protocol/v2_table1.py`：参考 `SonyTandemV2Table1Protocol.kt`、`SonyEqEbbPayloadParser.kt`
- `src/sonypodswin/transport/ble_transport.py`：参考 `SonyGattTandemSession.kt`、`SonyBleClient.kt` 的分片与 ACK/SEQ 机制
