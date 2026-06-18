using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConfigConnectionMode = EAPSimulator.Core.Configuration.ConnectionMode;
using EAPSimulator.Core.Configuration;
using EAPSimulator.Core.Protocols.HostProtocol;
using Newtonsoft.Json;

namespace EAPSimulator.UI.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private CancellationTokenSource? _autoSaveCts;
    // Auto-save state
    private bool _hasSaved = false;
    private bool _showSaveSuccess = false;

    public bool HasSaved
    {
        get => _hasSaved;
        set => SetProperty(ref _hasSaved, value);
    }

    public bool ShowSaveSuccess
    {
        get => _showSaveSuccess;
        set => SetProperty(ref _showSaveSuccess, value);
    }

    public ConfigViewModel()
    {
        // Watch for any property change to trigger auto-save
        PropertyChanged += (_, _) => ScheduleAutoSave();

        // Load saved config for default mode on startup
        LoadSecsConfigForMode(ConnectionMode);
        LoadCustomConfig();
        LoadHostChannels();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token); // debounce 1 second
                SaveAllConfigs();
                ShowSaveSuccess = true;
                HasSaved = true;
                // Hide success message after 3 seconds
                await Task.Delay(3000);
                ShowSaveSuccess = false;
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private void SaveAllConfigs()
    {
        SaveSecsConfig();
        SaveCustomConfig();
        SaveHostChannels();
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

    // Validation
    [ObservableProperty]
    private string _validationMessage;

    public bool HasValidationErrors => !string.IsNullOrWhiteSpace(ValidationMessage);

    // ─── Host (MES/RMS) protocol settings ───

    [ObservableProperty]
    private bool _hostEnabled;

    /// <summary>HttpPost / Mqtt / Tcp / Kafka / RabbitMq / Grpc / OpcUa / ActiveMq.</summary>
    [ObservableProperty]
    private string _hostTransportType = "HttpPost";

    /// <summary>true = client (EAP connects out to MES); false = passive listener.</summary>
    [ObservableProperty]
    private bool _hostIsActiveMode = true;

    [ObservableProperty]
    private string _hostHttpUrl = "http://127.0.0.1:8080/api/mes";

    [ObservableProperty]
    private string _hostRemoteHost = "127.0.0.1";

    [ObservableProperty]
    private int _hostRemotePort = 8080;

    [ObservableProperty]
    private string _hostMessageTemplatesPath = "host_message_templates.json";

    public string[] HostTransportTypes { get; } =
        ["HttpPost", "Tcp", "Mqtt", "Kafka", "RabbitMq", "ActiveMq", "Grpc", "OpcUa"];

    /// <summary>
    /// Get the persistent config directory (project root, not bin output).
    /// </summary>
    private static string GetConfigDirectory()
    {
        // Try to find the project root by looking for .sln file
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: use AppData
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EAPSimulator");
        Directory.CreateDirectory(appData);
        return appData;
    }

    [RelayCommand(CanExecute = nameof(CanSaveConfig))]
    private async Task SaveConfig()
    {
        if (!ValidateConfiguration())
        {
            await ShowMessageAsync("Validation Error", ValidationMessage, MessageKind.Error);
            return;
        }

        SaveAllConfigs();
        ShowSaveSuccess = true;
        HasSaved = true;

        await ShowMessageAsync("Success", "Configuration saved successfully!", MessageKind.Success);
    }

    private enum MessageKind { Info, Warning, Error, Success }

    private async Task ShowMessageAsync(string title, string message, MessageKind kind)
    {
        var window = GetMainWindow();
        if (window == null) return;

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SystemDecorations = SystemDecorations.Full,
            ShowInTaskbar = false,
        };

        var icon = kind switch
        {
            MessageKind.Error => "⚠",
            MessageKind.Warning => "⚠",
            MessageKind.Success => "✓",
            _ => "ℹ"
        };

        var iconColor = kind switch
        {
            MessageKind.Error => "#D32F2F",
            MessageKind.Warning => "#F57C00",
            MessageKind.Success => "#388E3C",
            _ => "#1976D2"
        };

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = icon, FontSize = 24, Foreground = Avalonia.Media.Brush.Parse(iconColor), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                        new TextBlock { Text = message, FontSize = 14, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MaxWidth = 300 }
                    }
                },
                new Button
                {
                    Content = "OK",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Padding = new Thickness(24, 8),
                    Command = new RelayCommand(() => dialog.Close())
                }
            }
        };

        dialog.Content = content;
        await dialog.ShowDialog(window);
    }

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    [RelayCommand]
    private void ResetConfig()
    {
        // Reset HSMS settings
        LocalHost = "0.0.0.0";
        LocalPort = 5000;
        RemoteHost = "127.0.0.1";
        RemotePort = 5000;
        DeviceId = 1;
        ConnectionMode = "Passive";
        AcceptCommunication = true;

        // Reset timeouts
        T3Timeout = 45000;
        T5Timeout = 10000;
        T6Timeout = 5000;
        T7Timeout = 10000;
        T8Timeout = 5000;

        // Reset custom protocol
        CustomHost = "127.0.0.1";
        CustomPort = 6000;
        CustomConfigPath = "custom_protocol.json";
        CustomIsServer = true;

        // Reset Host channels to default
        HostChannels.Clear();
        HostChannels.Add(new HostChannelViewModel
        {
            Name = "MES",
            TransportType = "HttpPost",
            HttpUrl = "http://127.0.0.1:8080/api/mes"
        });

        ValidationMessage = null;
        ShowSaveSuccess = false;
        HasSaved = false;
    }

    private bool CanSaveConfig()
    {
        return !HasValidationErrors;
    }

    private bool ValidateConfiguration()
    {
        var errors = new List<string>();

        // Validate IP addresses
        if (!IsValidIp(LocalHost))
            errors.Add("Local IP is invalid");

        if (!IsValidIp(RemoteHost))
            errors.Add("Remote IP is invalid");

        if (!IsValidIp(CustomHost))
            errors.Add("Custom Host IP is invalid");

        // Validate ports
        if (LocalPort < 1 || LocalPort > 65535)
            errors.Add("Local Port must be between 1 and 65535");

        if (RemotePort < 1 || RemotePort > 65535)
            errors.Add("Remote Port must be between 1 and 65535");

        if (CustomPort < 1 || CustomPort > 65535)
            errors.Add("Custom Port must be between 1 and 65535");

        // Validate timeouts
        if (T3Timeout < 1000 || T3Timeout > 300000)
            errors.Add("T3 Timeout must be between 1000 and 300000 ms");

        if (T5Timeout < 1000 || T5Timeout > 60000)
            errors.Add("T5 Timeout must be between 1000 and 60000 ms");

        ValidationMessage = errors.Count > 0 ? string.Join("\n", errors) : null;
        return errors.Count == 0;
    }

    private bool IsValidIp(string ip)
    {
        return ip == "0.0.0.0" || IPAddress.TryParse(ip, out _);
    }

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
        var path = Path.Combine(GetConfigDirectory(), fileName);

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
            var path = Path.Combine(GetConfigDirectory(), "custom_settings.json");
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
            var path = Path.Combine(GetConfigDirectory(), "custom_settings.json");
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

        var path = Path.Combine(GetConfigDirectory(), fileName);
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

    /// <summary>
    /// Build a <see cref="EAPSimulator.Core.Protocols.HostProtocol.HostTransportConfig"/> from the
    /// current Host fields. Caller is responsible for checking <see cref="HostEnabled"/>.
    /// </summary>
    public EAPSimulator.Core.Protocols.HostProtocol.HostTransportConfig GetHostTransportConfig()
    {
        var transport = Enum.TryParse<EAPSimulator.Core.Protocols.HostProtocol.TransportType>(HostTransportType, true, out var tt)
            ? tt
            : EAPSimulator.Core.Protocols.HostProtocol.TransportType.HttpPost;
        return new EAPSimulator.Core.Protocols.HostProtocol.HostTransportConfig
        {
            TransportType = transport,
            IsActiveMode = HostIsActiveMode,
            RemoteHost = HostRemoteHost,
            RemotePort = HostRemotePort,
            HttpUrl = HostHttpUrl,
        };
    }

    // ─── Multi-channel Host config ───
    // Each channel is one named connection (MES, RMS, WMS, ...). Persisted to
    // host_channels.json next to the SECS config.

    public ObservableCollection<HostChannelViewModel> HostChannels { get; } = [];

    private string HostChannelsPath => Path.Combine(GetConfigDirectory(), "host_channels.json");

    public void LoadHostChannels()
    {
        var path = HostChannelsPath;
        try
        {
            if (File.Exists(path))
            {
                var coll = HostChannelCollection.LoadFromFile(path);
                HostChannels.Clear();
                foreach (var c in coll.Channels)
                    HostChannels.Add(HostChannelViewModel.FromModel(c));
                return;
            }
        }
        catch { }

        // Seed with one default channel if no file exists
        if (HostChannels.Count == 0)
            HostChannels.Add(new HostChannelViewModel {
                Name = "MES",
                TransportType = "HttpPost",
                HttpUrl = "http://127.0.0.1:8080/api/mes"
            });
    }

    [RelayCommand]
    private async Task RemoveHostChannel(HostChannelViewModel? channel)
    {
        if (channel == null) return;

        var result = await ShowConfirmDialogAsync(
            "Confirm Delete",
            $"Are you sure you want to remove the channel '{channel.Name}'?\nAll associated data will be lost.");

        if (result)
        {
            HostChannels.Remove(channel);
            SaveHostChannels();
        }
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window == null) return false;

        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SystemDecorations = SystemDecorations.Full,
            ShowInTaskbar = false,
        };

        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "⚠", FontSize = 24, Foreground = Avalonia.Media.Brush.Parse("#F57C00"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                        new TextBlock { Text = message, FontSize = 14, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MaxWidth = 300 }
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 12,
                    Children =
                    {
                        new Button
                        {
                            Content = "否",
                            Padding = new Thickness(24, 8),
                            Classes = { "secondary" },
                            Command = new RelayCommand(() =>
                            {
                                tcs.TrySetResult(false);
                                dialog.Close();
                            })
                        },
                        new Button
                        {
                            Content = "是",
                            Padding = new Thickness(24, 8),
                            Classes = { "accent" },
                            Command = new RelayCommand(() =>
                            {
                                tcs.TrySetResult(true);
                                dialog.Close();
                            })
                        }
                    }
                }
            }
        };

        dialog.Content = content;
        dialog.Closed += (_, _) => { if (!tcs.Task.IsCompleted) tcs.SetResult(false); };
        await dialog.ShowDialog(window);
        return await tcs.Task;
    }

    [RelayCommand]
    private void AddHostChannel()
    {
        var newChannel = new HostChannelViewModel
        {
            Name = $"Channel{HostChannels.Count + 1}",
            TransportType = "HttpPost",
            HttpUrl = "http://127.0.0.1:8080/api",
            IsActiveMode = true,
            RemotePort = 8080,
            BodyFormat = "Json",
            ContentType = "application/json"
        };
        HostChannels.Add(newChannel);
        SaveHostChannels();
    }

    public void SaveHostChannels()
    {
        try
        {
            var coll = new HostChannelCollection
            {
                Channels = HostChannels.Select(c => c.ToModel()).ToList(),
            };
            coll.SaveToFile(HostChannelsPath);
        }
        catch { }
    }
}

/// <summary>
/// Per-channel config + connection state binding model. Connection actions are wired
/// up by MainViewModel via the <see cref="ConnectRequested"/>/<see cref="DisconnectRequested"/>
/// events so this class stays decoupled from the transport stack.
/// </summary>
public partial class HostChannelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "MES";

    [ObservableProperty]
    private string _transportType = "HttpPost";

    [ObservableProperty]
    private bool _isActiveMode = true;

    [ObservableProperty]
    private string _bodyFormat = "Json";

    [ObservableProperty]
    private string _templatePath = "";

    // ─── HTTP ───
    [ObservableProperty] private string _httpUrl = "";
    [ObservableProperty] private string _contentType = "application/json";

    /// <summary>
    /// Custom HTTP headers attached to every outbound request (Active mode). Most users
    /// fill this in to add bearer / api-key authentication, e.g.
    /// <c>Authorization: Bearer xxx</c> or <c>X-Api-Key: xxx</c>. Empty rows are ignored
    /// when serialized to <see cref="HostChannelConfig.HttpHeaders"/>.
    /// </summary>
    public ObservableCollection<HttpHeaderViewModel> HttpHeaders { get; } = new();

    // ─── TCP ───
    [ObservableProperty] private string _remoteHost = "127.0.0.1";
    [ObservableProperty] private int _remotePort = 8080;
    [ObservableProperty] private string _localHost = "0.0.0.0";
    [ObservableProperty] private int _localPort = 0;

    // ─── MQTT ───
    [ObservableProperty] private string _mqttBroker = "localhost";
    [ObservableProperty] private int _mqttPort = 1883;
    [ObservableProperty] private string _mqttTopic = "eap/mes/messages";
    [ObservableProperty] private string _mqttClientId = "";

    // ─── Kafka ───
    [ObservableProperty] private string _kafkaBootstrapServers = "localhost:9092";
    [ObservableProperty] private string _kafkaTopic = "eap-mes-topic";
    [ObservableProperty] private string _kafkaGroupId = "eap-group";

    // ─── RabbitMQ ───
    [ObservableProperty] private string _rabbitMqHost = "localhost";
    [ObservableProperty] private int _rabbitMqPort = 5672;
    [ObservableProperty] private string _rabbitMqExchange = "eap.exchange";
    [ObservableProperty] private string _rabbitMqRoutingKey = "eap.mes";
    [ObservableProperty] private string _rabbitMqQueue = "eap.mes.queue";

    // ─── ActiveMQ ───
    [ObservableProperty] private string _activeMqBrokerUri = "tcp://localhost:61616";
    [ObservableProperty] private string _activeMqQueue = "eap.mes.queue";
    [ObservableProperty] private string _activeMqResponseQueue = "eap.response.queue";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";

    // ─── gRPC ───
    [ObservableProperty] private string _grpcEndpoint = "https://localhost:5001";

    // ─── OPC UA ───
    [ObservableProperty] private string _opcUaEndpoint = "opc.tcp://localhost:4840";

    // ─── Runtime state (not persisted) ───

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "未连接";

    [ObservableProperty]
    private string _statusColor = "#888";

    /// <summary>展开/折叠详细配置。默认折叠，节省界面空间。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    // ─── Validation ───

    [ObservableProperty]
    private string _validationErrors = "";

    public bool HasValidationErrors => !string.IsNullOrWhiteSpace(ValidationErrors);

    partial void OnValidationErrorsChanged(string value) => OnPropertyChanged(nameof(HasValidationErrors));

    /// <summary>连接测试中</summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>最后一次测试结果文本（成功绿色/失败红色）</summary>
    [ObservableProperty]
    private string _lastTestResult = "";

    public bool HasTestResult => !string.IsNullOrWhiteSpace(LastTestResult);

    partial void OnLastTestResultChanged(string value) => OnPropertyChanged(nameof(HasTestResult));

    [ObservableProperty]
    private bool _lastTestSuccess;

    // ─── Runtime statistics (not persisted) ───

    [ObservableProperty]
    private int _sendCount;

    [ObservableProperty]
    private int _receiveCount;

    [ObservableProperty]
    private string _lastLatency = "";

    [ObservableProperty]
    private string _lastError = "";

    public string[] TransportTypes { get; } =
        ["HttpPost", "Tcp", "Mqtt", "Kafka", "RabbitMq", "ActiveMq", "Grpc", "OpcUa"];

    public string[] BodyFormats { get; } = ["Json", "Raw"];

    public string[] ContentTypes { get; } =
    [
        "application/json",
        "application/xml",
        "text/plain",
        "text/xml",
        "application/x-www-form-urlencoded",
        "application/octet-stream",
    ];

    // ─── Per-protocol visibility flags（让 XAML 简单地用 IsVisible 绑定）───
    public bool IsHttp     => TransportType == "HttpPost";
    public bool IsTcp      => TransportType == "Tcp";
    public bool IsMqtt     => TransportType == "Mqtt";
    public bool IsKafka    => TransportType == "Kafka";
    public bool IsRabbitMq => TransportType == "RabbitMq";
    public bool IsActiveMq => TransportType == "ActiveMq";
    public bool IsGrpc     => TransportType == "Grpc";
    public bool IsOpcUa    => TransportType == "OpcUa";

    /// <summary>仅 TCP 协议有"主动/被动"模式的概念。</summary>
    public bool ShowActiveModeSwitch => IsTcp;

    /// <summary>头部展示的端点摘要（折叠态用）</summary>
    public string EndpointSummary => TransportType switch
    {
        "HttpPost"  => string.IsNullOrWhiteSpace(HttpUrl) ? "(未配置)" : HttpUrl,
        "Tcp"       => $"{RemoteHost}:{RemotePort}",
        "Mqtt"      => $"{MqttBroker}:{MqttPort} / {MqttTopic}",
        "Kafka"     => $"{KafkaBootstrapServers} / {KafkaTopic}",
        "RabbitMq"  => $"{RabbitMqHost}:{RabbitMqPort} / {RabbitMqExchange}",
        "ActiveMq"  => $"{ActiveMqBrokerUri} / {ActiveMqQueue}",
        "Grpc"      => GrpcEndpoint,
        "OpcUa"     => OpcUaEndpoint,
        _           => "",
    };

    partial void OnTransportTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsHttp));
        OnPropertyChanged(nameof(IsTcp));
        OnPropertyChanged(nameof(IsMqtt));
        OnPropertyChanged(nameof(IsKafka));
        OnPropertyChanged(nameof(IsRabbitMq));
        OnPropertyChanged(nameof(IsActiveMq));
        OnPropertyChanged(nameof(IsGrpc));
        OnPropertyChanged(nameof(IsOpcUa));
        OnPropertyChanged(nameof(ShowActiveModeSwitch));
        OnPropertyChanged(nameof(EndpointSummary));
    }

    partial void OnHttpUrlChanged(string value)               => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnRemoteHostChanged(string value)            => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnRemotePortChanged(int value)               => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnMqttBrokerChanged(string value)            => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnMqttPortChanged(int value)                 => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnMqttTopicChanged(string value)             => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnKafkaBootstrapServersChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnKafkaTopicChanged(string value)            => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnRabbitMqHostChanged(string value)          => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnRabbitMqPortChanged(int value)             => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnRabbitMqExchangeChanged(string value)      => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnActiveMqBrokerUriChanged(string value)     => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnActiveMqQueueChanged(string value)         => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnGrpcEndpointChanged(string value)          => OnPropertyChanged(nameof(EndpointSummary));
    partial void OnOpcUaEndpointChanged(string value)         => OnPropertyChanged(nameof(EndpointSummary));

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void AddHeader() => HttpHeaders.Add(new HttpHeaderViewModel(HttpHeaders));

    /// <summary>Raised when the user clicks the channel's Connect button.</summary>
    public event Func<HostChannelViewModel, Task>? ConnectRequested;

    /// <summary>Raised when the user clicks Disconnect.</summary>
    public event Func<HostChannelViewModel, Task>? DisconnectRequested;

    [RelayCommand]
    private async Task Connect()
    {
        // 先验证
        var errors = Validate();
        if (errors.Count > 0)
        {
            ValidationErrors = string.Join("\n", errors);
            return;
        }
        ValidationErrors = "";

        if (ConnectRequested != null) await ConnectRequested.Invoke(this);
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        if (DisconnectRequested != null) await DisconnectRequested.Invoke(this);
    }

    /// <summary>
    /// 测试连接：尝试建立连接后立即断开，验证配置是否正确。
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            ValidationErrors = string.Join("\n", errors);
            LastTestResult = "❌ 配置有误，请先修复";
            LastTestSuccess = false;
            return;
        }
        ValidationErrors = "";

        IsTesting = true;
        LastTestResult = "⏳ 正在测试...";
        LastTestSuccess = false;

        try
        {
            // 实际测试连接逻辑（由 MainViewModel 通过事件处理）
            if (TestConnectionRequested != null)
            {
                var (success, message) = await TestConnectionRequested.Invoke(this);
                LastTestResult = success ? $"✅ {message}" : $"❌ {message}";
                LastTestSuccess = success;
            }
            else
            {
                // 没有处理器时模拟测试
                await Task.Delay(500);
                LastTestResult = "⚠ 测试连接功能尚未实现";
                LastTestSuccess = false;
            }
        }
        catch (Exception ex)
        {
            LastTestResult = $"❌ 测试失败: {ex.Message}";
            LastTestSuccess = false;
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>Raised when the user clicks the Test Connection button.</summary>
    public event Func<HostChannelViewModel, Task<(bool success, string message)>>? TestConnectionRequested;

    [RelayCommand]
    private void ResetStatistics()
    {
        SendCount = 0;
        ReceiveCount = 0;
        LastLatency = "";
        LastError = "";
    }

    /// <summary>
    /// 验证当前配置，返回错误列表。空列表表示验证通过。
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // 名称
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("通道名称不能为空");

        // 按协议验证
        switch (TransportType)
        {
            case "HttpPost":
                if (string.IsNullOrWhiteSpace(HttpUrl))
                    errors.Add("URL 不能为空");
                else if (!HttpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      && !HttpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    errors.Add("URL 必须以 http:// 或 https:// 开头");
                break;

            case "Tcp":
                if (string.IsNullOrWhiteSpace(RemoteHost))
                    errors.Add("远程地址不能为空");
                if (RemotePort < 1 || RemotePort > 65535)
                    errors.Add("远程端口必须在 1-65535 之间");
                if (!IsActiveMode)
                {
                    if (string.IsNullOrWhiteSpace(LocalHost))
                        errors.Add("本地地址不能为空");
                    if (LocalPort < 0 || LocalPort > 65535)
                        errors.Add("本地端口必须在 0-65535 之间");
                }
                break;

            case "Mqtt":
                if (string.IsNullOrWhiteSpace(MqttBroker))
                    errors.Add("Broker 地址不能为空");
                if (MqttPort < 1 || MqttPort > 65535)
                    errors.Add("Broker 端口必须在 1-65535 之间");
                if (string.IsNullOrWhiteSpace(MqttTopic))
                    errors.Add("Topic 不能为空");
                break;

            case "Kafka":
                if (string.IsNullOrWhiteSpace(KafkaBootstrapServers))
                    errors.Add("Bootstrap Servers 不能为空");
                if (string.IsNullOrWhiteSpace(KafkaTopic))
                    errors.Add("Topic 不能为空");
                break;

            case "RabbitMq":
                if (string.IsNullOrWhiteSpace(RabbitMqHost))
                    errors.Add("主机地址不能为空");
                if (RabbitMqPort < 1 || RabbitMqPort > 65535)
                    errors.Add("端口必须在 1-65535 之间");
                break;

            case "ActiveMq":
                if (string.IsNullOrWhiteSpace(ActiveMqBrokerUri))
                    errors.Add("Broker URI 不能为空");
                break;

            case "Grpc":
                if (string.IsNullOrWhiteSpace(GrpcEndpoint))
                    errors.Add("Endpoint 不能为空");
                else if (!GrpcEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      && !GrpcEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Endpoint 必须以 http:// 或 https:// 开头");
                break;

            case "OpcUa":
                if (string.IsNullOrWhiteSpace(OpcUaEndpoint))
                    errors.Add("Endpoint 不能为空");
                else if (!OpcUaEndpoint.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase))
                    errors.Add("OPC UA Endpoint 必须以 opc.tcp:// 开头");
                break;
        }

        return errors;
    }

    public HostChannelConfig ToModel() => new()
    {
        Name = Name,
        TransportType = TransportType,
        IsActiveMode = IsActiveMode,
        BodyFormat = BodyFormat,
        TemplatePath = TemplatePath,
        // HTTP
        HttpUrl = HttpUrl,
        ContentType = ContentType,
        HttpHeaders = HttpHeaders
            .Where(h => !string.IsNullOrWhiteSpace(h.Key))
            .ToDictionary(h => h.Key.Trim(), h => h.Value ?? "", StringComparer.OrdinalIgnoreCase),
        // TCP
        RemoteHost = RemoteHost,
        RemotePort = RemotePort,
        LocalHost = LocalHost,
        LocalPort = LocalPort,
        // MQTT
        MqttBroker = MqttBroker,
        MqttPort = MqttPort,
        MqttTopic = MqttTopic,
        MqttClientId = MqttClientId,
        // Kafka
        KafkaBootstrapServers = KafkaBootstrapServers,
        KafkaTopic = KafkaTopic,
        KafkaGroupId = KafkaGroupId,
        // RabbitMQ
        RabbitMqHost = RabbitMqHost,
        RabbitMqPort = RabbitMqPort,
        RabbitMqExchange = RabbitMqExchange,
        RabbitMqRoutingKey = RabbitMqRoutingKey,
        RabbitMqQueue = RabbitMqQueue,
        // ActiveMQ
        ActiveMqBrokerUri = ActiveMqBrokerUri,
        ActiveMqQueue = ActiveMqQueue,
        ActiveMqResponseQueue = ActiveMqResponseQueue,
        Username = Username,
        Password = Password,
        // gRPC
        GrpcEndpoint = GrpcEndpoint,
        // OPC UA
        OpcUaEndpoint = OpcUaEndpoint,
    };

    public static HostChannelViewModel FromModel(HostChannelConfig c)
    {
        var vm = new HostChannelViewModel
        {
            Name = c.Name,
            TransportType = c.TransportType,
            IsActiveMode = c.IsActiveMode,
            BodyFormat = c.BodyFormat,
            TemplatePath = c.TemplatePath,
            // HTTP
            HttpUrl = c.HttpUrl,
            ContentType = string.IsNullOrEmpty(c.ContentType) ? "application/json" : c.ContentType,
            // TCP
            RemoteHost = c.RemoteHost,
            RemotePort = c.RemotePort,
            LocalHost = c.LocalHost,
            LocalPort = c.LocalPort,
            // MQTT
            MqttBroker = c.MqttBroker,
            MqttPort = c.MqttPort,
            MqttTopic = c.MqttTopic,
            MqttClientId = c.MqttClientId,
            // Kafka
            KafkaBootstrapServers = c.KafkaBootstrapServers,
            KafkaTopic = c.KafkaTopic,
            KafkaGroupId = c.KafkaGroupId,
            // RabbitMQ
            RabbitMqHost = c.RabbitMqHost,
            RabbitMqPort = c.RabbitMqPort,
            RabbitMqExchange = c.RabbitMqExchange,
            RabbitMqRoutingKey = c.RabbitMqRoutingKey,
            RabbitMqQueue = c.RabbitMqQueue,
            // ActiveMQ
            ActiveMqBrokerUri = c.ActiveMqBrokerUri,
            ActiveMqQueue = c.ActiveMqQueue,
            ActiveMqResponseQueue = c.ActiveMqResponseQueue,
            Username = c.Username,
            Password = c.Password,
            // gRPC
            GrpcEndpoint = c.GrpcEndpoint,
            // OPC UA
            OpcUaEndpoint = c.OpcUaEndpoint,
        };
        // Headers list is a getter-only ObservableCollection — populate after construction.
        if (c.HttpHeaders != null)
        {
            foreach (var (k, v) in c.HttpHeaders)
                vm.HttpHeaders.Add(new HttpHeaderViewModel(vm.HttpHeaders) { Key = k, Value = v ?? "" });
        }
        return vm;
    }
}

/// <summary>
/// One HTTP header row (Key + Value) shown in the channel's Headers table. Empty Key
/// rows are filtered out at <see cref="HostChannelViewModel.ToModel"/> time, so the user
/// can leave a placeholder row open while editing without polluting the saved config.
/// Owns a delete command bound to its parent collection so the per-row "✕" button can
/// fire without crawling up the visual tree to find the channel VM.
/// </summary>
public partial class HttpHeaderViewModel : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";

    private readonly ObservableCollection<HttpHeaderViewModel>? _owner;

    public HttpHeaderViewModel() { }

    public HttpHeaderViewModel(ObservableCollection<HttpHeaderViewModel> owner)
    {
        _owner = owner;
    }

    [RelayCommand]
    private void Delete() => _owner?.Remove(this);
}
