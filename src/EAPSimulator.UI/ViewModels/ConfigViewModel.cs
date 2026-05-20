using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConfigConnectionMode = EAPSimulator.Core.Configuration.ConnectionMode;
using EAPSimulator.Core.Configuration;
using Newtonsoft.Json;

namespace EAPSimulator.UI.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private CancellationTokenSource? _autoSaveCts;
    private bool _suppressAutoSave;

    public ConfigViewModel()
    {
        // Watch for any property change to trigger auto-save

        PropertyChanged += (_, _) => ScheduleAutoSave();

        // Load saved config for default mode on startup
        LoadSecsConfigForMode(ConnectionMode);
        LoadCustomConfig();
    }

    private void ScheduleAutoSave()
    {
        if (_suppressAutoSave) return;
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token); // debounce 500ms
                SaveSecsConfig();
                SaveCustomConfig();
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    [ObservableProperty]
    private string _localHost = "0.0.0.0";

    [ObservableProperty]
    private int _localPort = 5000;

    [ObservableProperty]
    private string _remoteHost = "127.0.0.1";

    [ObservableProperty]
    private int _remotePort = 5000;

    [ObservableProperty]
    private ushort _deviceId = 1;

    [ObservableProperty]
    private string _connectionMode = "Passive";

    [ObservableProperty]
    private string _connectionModeHint = "本端等待对端主动连接。主要配置 Local IP/Port，确保对端的 Remote IP/Port 指向本端地址。";

    partial void OnConnectionModeChanged(string value)
    {
        ConnectionModeHint = value switch
        {
            "Passive" => "本端等待对端主动连接。主要配置 Local IP/Port，确保对端的 Remote IP/Port 指向本端地址。",
            "Active" => "本端主动连接对端。主要配置 Remote IP/Port（对端地址），Local IP/Port 用于绑定本端网卡。",
            "Alternating" => "双方均可主动连接，谁先连上谁赢。Local 和 Remote 地址都需要正确配置。",
            _ => ""
        };

        // Auto-load SECS/GEM config for this mode
        LoadSecsConfigForMode(value);
    }

    public string[] ConnectionModes { get; } = ["Passive", "Active", "Alternating"];

    [ObservableProperty]
    private int _t3Timeout = 45000;

    [ObservableProperty]
    private int _t5Timeout = 10000;

    [ObservableProperty]
    private int _t6Timeout = 5000;

    [ObservableProperty]
    private int _t7Timeout = 10000;

    [ObservableProperty]
    private int _t8Timeout = 5000;

    // Custom protocol settings
    [ObservableProperty]
    private string _customHost = "127.0.0.1";

    [ObservableProperty]
    private int _customPort = 6000;

    [ObservableProperty]
    private string _customConfigPath = "custom_protocol.json";

    [ObservableProperty]
    private bool _customIsServer = true;

    [ObservableProperty]
    private bool _acceptCommunication = true;

    [RelayCommand]
    private void SaveSecsConfig()
    {
        var fileName = ConnectionMode.ToLower() switch
        {
            "passive" => "secs_gem_passive.json",
            "active" => "secs_gem_active.json",
            "alternating" => "secs_gem_alternating.json",
            _ => "secs_gem_config.json"
        };
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

        var settings = new HsmsSettings
        {
            LocalHost = LocalHost,
            LocalPort = LocalPort,
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            DeviceId = DeviceId,
            ConnectionMode = Enum.TryParse<ConfigConnectionMode>(ConnectionMode, true, out var m) ? m : ConfigConnectionMode.Passive,
            T3Timeout = T3Timeout,
            T5Timeout = T5Timeout,
            T6Timeout = T6Timeout,
            T7Timeout = T7Timeout,
            T8Timeout = T8Timeout,
        };

        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    private void SaveCustomConfig()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_settings.json");
            var settings = new
            {
                CustomHost,
                CustomPort,
                CustomConfigPath,
                CustomIsServer,
            };
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    private void LoadCustomConfig()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_settings.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var obj = JsonConvert.DeserializeAnonymousType(json, new
            {
                CustomHost = "",
                CustomPort = 0,
                CustomConfigPath = "",
                CustomIsServer = true,
            });
            if (obj == null) return;
            if (!string.IsNullOrEmpty(obj.CustomHost)) CustomHost = obj.CustomHost;
            if (obj.CustomPort > 0) CustomPort = obj.CustomPort;
            if (!string.IsNullOrEmpty(obj.CustomConfigPath)) CustomConfigPath = obj.CustomConfigPath;
            CustomIsServer = obj.CustomIsServer;
        }
        catch { }
    }

    private void LoadSecsConfigForMode(string mode)
    {
        var fileName = mode.ToLower() switch
        {
            "passive" => "secs_gem_passive.json",
            "active" => "secs_gem_active.json",
            "alternating" => "secs_gem_alternating.json",
            _ => null
        };
        if (fileName == null) return;

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonConvert.DeserializeObject<HsmsSettings>(json);
            if (settings != null)
                ApplyHsmsSettings(settings, keepMode: true);
        }
        catch { }
    }

    public HsmsSettings GetHsmsSettings()
    {
        Enum.TryParse<ConnectionMode>(ConnectionMode, true, out var mode);
        return new HsmsSettings
        {
            LocalHost = LocalHost,
            LocalPort = LocalPort,
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            DeviceId = DeviceId,
            ConnectionMode = mode,
            T3Timeout = T3Timeout,
            T5Timeout = T5Timeout,
            T6Timeout = T6Timeout,
            T7Timeout = T7Timeout,
            T8Timeout = T8Timeout,
        };
    }

    public void ApplyHsmsSettings(HsmsSettings settings, bool keepMode = false)
    {
        LocalHost = settings.LocalHost;
        LocalPort = settings.LocalPort;
        RemoteHost = settings.RemoteHost;
        RemotePort = settings.RemotePort;
        DeviceId = settings.DeviceId;
        if (!keepMode)
            ConnectionMode = settings.ConnectionMode.ToString();
        T3Timeout = settings.T3Timeout;
        T5Timeout = settings.T5Timeout;
        T6Timeout = settings.T6Timeout;
        T7Timeout = settings.T7Timeout;
        T8Timeout = settings.T8Timeout;
    }
}
