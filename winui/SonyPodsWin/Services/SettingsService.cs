using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonyPodsWin.ViewModels;

namespace SonyPodsWin.Services;

/// <summary>
/// 持久化服务：读写 ~/.sonypodswin.json（逐项对齐 parity-checklist.md §3）。
/// 仅持久化 8 个应用/联动键，耳机本体侧设置（降噪/EQ/DSEE/多点等）绝不落盘。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ConfigFilePath { get; }

    public SettingsService(string? customPath = null)
    {
        ConfigFilePath = string.IsNullOrWhiteSpace(customPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sonypodswin.json")
            : customPath;
    }

    /// <summary>
    /// 读取 ~/.sonypodswin.json。文件不存在或解析/IO 异常时静默返回 null，调用方保留控件默认值。
    /// </summary>
    public SettingsSnapshot? Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
            PersistedConfigDto? dto = JsonSerializer.Deserialize<PersistedConfigDto>(json, JsonOptions);
            if (dto is null)
            {
                return null;
            }

            // 与 parity-checklist.md §3 逐项对齐：
            // - default_volume 读取时 int 直用不做范围 clamp
            // - low_battery_threshold 读取时 clamp 到 5..50
            return new SettingsSnapshot(
                LastName: string.IsNullOrWhiteSpace(dto.LastName) ? "WH-1000XM6" : dto.LastName,
                LastAddress: string.IsNullOrWhiteSpace(dto.LastAddress) ? null : dto.LastAddress,
                ShowPopupOnConnect: dto.ShowPopupOnConnect ?? true,
                AutoReconnect: false,
                AutoPlay: dto.AutoPlay ?? false,
                AutoPause: dto.AutoPause ?? true,
                DefaultVolume: dto.DefaultVolume ?? 54,
                LowBatteryThreshold: Math.Clamp(dto.LowBatteryThreshold ?? 30, 5, 50));
        }
        catch
        {
            // 与 Python 原版一致：解析/读取异常整体静默忽略
            return null;
        }
    }

    public void Save(SettingsSnapshot snapshot)
    {
        try
        {
            var dto = new PersistedConfigDto
            {
                LastName = snapshot.LastName,
                LastAddress = snapshot.LastAddress,
                ShowPopupOnConnect = snapshot.ShowPopupOnConnect,
                AutoReconnect = snapshot.AutoReconnect,
                AutoPlay = snapshot.AutoPlay,
                AutoPause = snapshot.AutoPause,
                DefaultVolume = snapshot.DefaultVolume,
                LowBatteryThreshold = snapshot.LowBatteryThreshold,
            };

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(ConfigFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // 落盘失败不阻断 UI 交互
        }
    }

    private sealed class PersistedConfigDto
    {
        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("last_address")]
        public string? LastAddress { get; set; }

        [JsonPropertyName("show_popup_on_connect")]
        public bool? ShowPopupOnConnect { get; set; }

        [JsonPropertyName("auto_reconnect")]
        public bool? AutoReconnect { get; set; }

        [JsonPropertyName("auto_play")]
        public bool? AutoPlay { get; set; }

        [JsonPropertyName("auto_pause")]
        public bool? AutoPause { get; set; }

        [JsonPropertyName("default_volume")]
        public int? DefaultVolume { get; set; }

        [JsonPropertyName("low_battery_threshold")]
        public int? LowBatteryThreshold { get; set; }
    }
}
