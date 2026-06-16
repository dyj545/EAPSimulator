using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAPSimulator.Core.Configuration;
using EAPSimulator.Core.Protocols;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Hsms;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILoggerFactory _loggerFactory;
    private IProtocol? _currentProtocol;
    private CancellationTokenSource? _cts;

    public IProtocol? CurrentProtocol => _currentProtocol;

    public ConfigViewModel Config { get; } = new();
    public MessageLogViewModel MessageLog { get; } = new();
    public StatusPanelViewModel StatusPanel { get; } = new();
    public MessageEditorViewModel MessageEditor { get; } = new();
    public AutoReplyViewModel AutoReply { get; } = new();

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private string _selectedProtocolType = "SECS/GEM";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _connectButtonText = "Start Listening";

    [ObservableProperty]
    private string _connectButtonColor = "#4CAF50";

    public string[] ProtocolTypes { get; } = ["SECS/GEM", "Custom"];

    partial void OnSelectedProtocolTypeChanged(string value)
    {
        UpdateButtonText();
    }

    private void UpdateButtonText()
    {
        if (IsConnected)
        {
            ConnectButtonText = "Connected";
            ConnectButtonColor = "#888";
        }
        else if (IsListening)
        {
            ConnectButtonText = "Stop Listening";
            ConnectButtonColor = "#FF9800";
        }
        else
        {
            ConnectButtonText = Config.ConnectionMode == "Passive" ? "Start Listening" : "Connect";
            ConnectButtonColor = "#4CAF50";
        }
    }

    private string _connectionMode = "";

    private void LogSystem(string content)
    {
        var tag = !string.IsNullOrEmpty(_connectionMode) ? $"[{_connectionMode}] " : "";
        MessageLog.AddEntry(new MessageLogEntry
        {
            Timestamp = DateTime.Now,
            Direction = "SYS",
            MessageId = "-",
            Protocol = "System",
            Content = $"{tag}{content}",
            Detail = $"{tag}{content}",
        });
    }

    public MainViewModel(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;

        // Wire up editor send event
        MessageEditor.SendMessageRequested += async msgVm =>
        {
            await SendEditorMessageAsync(msgVm);
        };

        // Wire up GEM state switch from StatusPanel
        StatusPanel.GemStateChangeRequested += async (_, targetState) =>
        {
            await HandleGemStateChangeAsync(targetState);
        };

        // Auto-update button text when Config.ConnectionMode changes
        Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConfigViewModel.ConnectionMode))
            {
                UpdateButtonText();
                SyncAutoReplyRole();
            }
        };

        // Initial role sync from current Config.
        SyncAutoReplyRole();

        // Auto-load default template file
        LoadDefaultTemplateFile();
    }

    /// <summary>
    /// Mirror Config.ConnectionMode → AutoReply.CurrentRole so new scenarios default to the
    /// right side and AutoStart filtering works without the user reconnecting first.
    /// </summary>
    private void SyncAutoReplyRole()
    {
        AutoReply.CurrentRole = Config.ConnectionMode == "Active"
            ? Core.Protocols.ProtocolRole.Host
            : Core.Protocols.ProtocolRole.Equipment;
    }

    private void LoadDefaultTemplateFile()
    {
        // Prefer project root over bin directory to avoid divergent copies
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var projectRootPath = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "..", "secs_message_templates.json"));
        var binPath = Path.Combine(basePath, "secs_message_templates.json");

        LogSystem($"[TemplatePath] BaseDir: {basePath}");
        LogSystem($"[TemplatePath] ProjectRoot: {projectRootPath} exists={File.Exists(projectRootPath)}");
        LogSystem($"[TemplatePath] BinPath: {binPath} exists={File.Exists(binPath)}");

        var templatePath = File.Exists(projectRootPath) ? projectRootPath : binPath;

        if (File.Exists(templatePath))
        {
            MessageEditor.LoadFromFile(templatePath);
            LogSystem($"已加载 {MessageEditor.AllMessages.Count} 条消息模板");

            // Pass templates to AutoReply module for template picker
            AutoReply.SetTemplates(MessageEditor.AllMessages.Select(m => m.ToTemplate()));
            AutoReply.SetAllMessages(MessageEditor.AllMessages);
            AutoReply.LoadDefault();
        }
        else
        {
            LogSystem("未找到消息模板文件，请点击左侧文件选择按钮加载 secs_message_templates.json");
        }
    }

    /// <summary>
    /// Send a message built from the editor's tree view model.
    /// Called by Ctrl+S shortcut and the editor's Send button.
    /// </summary>
    public async Task SendEditorMessageAsync(SecsMessageViewModel msgVm)
    {
        if (_currentProtocol == null)
        {
            var state = IsListening ? "正在监听，等待对方连接" :
                        IsConnected ? "已连接但协议实例为空(异常)" :
                        "未连接 - 请点击 Connect 或 Start Listening";
            LogSystem($"ERROR: 无法发送消息。当前状态: {state}");
            return;
        }

        if (_currentProtocol.State != ProtocolState.Connected &&
            _currentProtocol.State != ProtocolState.Online)
        {
            LogSystem($"ERROR: 协议已创建但未建立有效连接。State={_currentProtocol.State}，请等待连接建立后再发送。");
            return;
        }

        if (_currentProtocol is SecsGemProtocol secsGem)
        {
            try
            {
                var msg = msgVm.ToSecsMessage();
                await secsGem.SendSecsMessageAsync(msg, CancellationToken.None);
                MessageEditor.StatusMessage = $"已发送 S{msgVm.Stream}F{msgVm.Function}";
            }
            catch (Exception ex)
            {
                LogSystem($"ERROR 发送 S{msgVm.Stream}F{msgVm.Function}: {ex.Message}");
                MessageEditor.StatusMessage = $"发送失败: {ex.Message}";
            }
        }
        else
        {
            LogSystem("ERROR: 当前协议不支持 SECS 消息发送。");
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsListening)
        {
            await StopListeningAsync();
            return;
        }

        if (IsConnected) return;

        try
        {
            _cts = new CancellationTokenSource();
            StatusText = "Starting...";
            StatusPanel.ProtocolType = SelectedProtocolType;

            LogSystem($"Starting {SelectedProtocolType} ({Config.ConnectionMode} mode)...");
            _connectionMode = Config.ConnectionMode;

            if (SelectedProtocolType == "SECS/GEM")
            {
                await ConnectSecsGemAsync();
            }
            else
            {
                await ConnectCustomAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            IsConnected = false;
            IsListening = false;
            UpdateButtonText();
            LogSystem($"ERROR: {ex.Message}");
            _loggerFactory.CreateLogger<MainViewModel>().LogError(ex, "Connection failed");
        }
    }

    private async Task StopListeningAsync()
    {
        try
        {
            if (_currentProtocol != null)
            {
                await _currentProtocol.StopAsync(CancellationToken.None);
                await _currentProtocol.DisposeAsync();
            }
        }
        catch { }

        _currentProtocol = null;
        IsConnected = false;
        IsListening = false;
        StatusPanel.UpdateConnectionState(false);
        StatusText = "Stopped";
        UpdateButtonText();
        LogSystem("Stopped listening.");
        _cts?.Cancel();
    }

    private async Task ConnectSecsGemAsync()
    {
        var settings = Config.GetHsmsSettings();

        var equipModel = new EquipmentModel
        {
            AcceptCommunication = Config.AcceptCommunication
        };

        var protocol = new SecsGemProtocol(
            _loggerFactory.CreateLogger<SecsGemProtocol>(),
            _loggerFactory.CreateLogger<HsmsTransport>(),
            _loggerFactory.CreateLogger<MessageRouter>(),
            settings,
            equipModel);

        protocol.MessageReceived += OnMessageReceived;
        protocol.MessageSent += OnMessageSent;
        protocol.StateChanged += OnProtocolStateChanged;

        // Register auto-reply rules on the router; pass the protocol's send method so
        // scenario Send/Reply steps can push messages out.
        AutoReply.ApplyToRouter(protocol.Router, _loggerFactory.CreateLogger<MainViewModel>(),
            (msg, ct) => protocol.SendSecsMessageAsync(msg, ct));

        await protocol.StartAsync(_cts!.Token);

        _currentProtocol = protocol;

        switch (settings.ConnectionMode)
        {
            case ConnectionMode.Passive:
                // Passive mode: StartAsync only starts listening, state stays Connecting.
                // Status will update to Connected when a peer connects via OnProtocolStateChanged.
                IsListening = true;
                StatusText = $"Listening on {settings.LocalHost}:{settings.LocalPort}...";
                LogSystem($"SECS/GEM Passive listening on {settings.LocalHost}:{settings.LocalPort}, waiting for peer connection.");
                break;
            case ConnectionMode.Active:
                IsConnected = true;
                StatusPanel.UpdateConnectionState(true);
                StatusText = $"Connected to {settings.RemoteHost}:{settings.RemotePort}";
                LogSystem($"SECS/GEM Active connected to {settings.RemoteHost}:{settings.RemotePort}.");
                break;
            case ConnectionMode.Alternating:
                // Alternating: ConnectAsync blocks until a connection is established
                IsConnected = true;
                StatusPanel.UpdateConnectionState(true);
                StatusText = "Alternating mode - connection established";
                LogSystem("SECS/GEM Alternating mode: connection established.");
                break;
        }

        UpdateButtonText();
        UpdateStatusPanelFromModel(equipModel);
    }

    private async Task ConnectCustomAsync()
    {
        var protocolDef = Core.Protocols.Custom.ProtocolDefinition.LoadFromFile(Config.CustomConfigPath);

        var protocol = new Core.Protocols.Custom.CustomProtocol(
            _loggerFactory.CreateLogger<Core.Protocols.Custom.CustomProtocol>(),
            _loggerFactory.CreateLogger<Core.Protocols.Custom.CustomTransport>(),
            protocolDef,
            Config.CustomHost,
            Config.CustomPort,
            Config.CustomIsServer);

        protocol.MessageReceived += OnMessageReceived;
        protocol.MessageSent += OnMessageSent;
        protocol.StateChanged += OnProtocolStateChanged;

        await protocol.StartAsync(_cts!.Token);

        _currentProtocol = protocol;

        if (!Config.CustomIsServer)
        {
            IsConnected = true;
            StatusPanel.UpdateConnectionState(true);
            StatusText = $"Connected to {Config.CustomHost}:{Config.CustomPort}";
            LogSystem($"Custom ({protocolDef.Name}) connected to {Config.CustomHost}:{Config.CustomPort}.");
        }
        else
        {
            IsListening = true;
            StatusText = $"Listening on {Config.CustomHost}:{Config.CustomPort}...";
            StatusPanel.UpdateConnectionState(false);
            LogSystem($"Custom ({protocolDef.Name}) listening on {Config.CustomHost}:{Config.CustomPort}, waiting for client.");
        }

        UpdateButtonText();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_currentProtocol == null) return;

        try
        {
            await _currentProtocol.StopAsync(CancellationToken.None);
            await _currentProtocol.DisposeAsync();
        }
        catch { }

        LogSystem("Disconnected.");
        _currentProtocol = null;
        IsConnected = false;
        IsListening = false;
        StatusPanel.UpdateConnectionState(false);
        StatusText = "Disconnected";
        _connectionMode = "";
        UpdateButtonText();
        _cts?.Cancel();
    }

    private void OnMessageReceived(object? sender, ProtocolMessageEventArgs e)
    {
        var secsItem = e.Message.GetField<SecsItem>("Items");
        var stream = e.Message.GetField<byte>("Stream");
        var function = e.Message.GetField<byte>("Function");
        var wBit = e.Message.GetField<bool>("WBit");
        var sysBytes = e.Message.GetField<uint>("SystemBytes");
        var brief = $"S{stream}F{function}{(wBit ? " W" : "")}";
        var proto = !string.IsNullOrEmpty(_connectionMode) ? $"SECS/{_connectionMode}" : "SECS/GEM";

        MessageLog.AddEntry(new MessageLogEntry
        {
            Timestamp = e.Timestamp,
            Direction = "<<",
            MessageId = e.Message.Id,
            Protocol = proto,
            Content = brief,
            Detail = FormatSecsDetail(e.Timestamp, "Incoming", sysBytes, stream, function, wBit, secsItem),
        });
    }

    private void OnMessageSent(object? sender, ProtocolMessageEventArgs e)
    {
        var secsItem = e.Message.GetField<SecsItem>("Items");
        var stream = e.Message.GetField<byte>("Stream");
        var function = e.Message.GetField<byte>("Function");
        var wBit = e.Message.GetField<bool>("WBit");
        var sysBytes = e.Message.GetField<uint>("SystemBytes");
        var brief = $"S{stream}F{function}{(wBit ? " W" : "")}";
        var proto = !string.IsNullOrEmpty(_connectionMode) ? $"SECS/{_connectionMode}" : "SECS/GEM";

        MessageLog.AddEntry(new MessageLogEntry
        {
            Timestamp = e.Timestamp,
            Direction = ">>",
            MessageId = e.Message.Id,
            Protocol = proto,
            Content = brief,
            Detail = FormatSecsDetail(e.Timestamp, "Outgoing", sysBytes, stream, function, wBit, secsItem),
        });
    }

    private string FormatSecsDetail(DateTime timestamp, string direction, uint sysBytes,
        byte stream, byte function, bool wBit, SecsItem? rootItem)
    {
        var sb = new System.Text.StringBuilder();

        // System bytes header
        var sysBytesStr = $"{sysBytes:X8}";
        var formatted = string.Join(" ", Enumerable.Range(0, 4).Select(i => sysBytesStr.Substring(i * 2, 2)));
        sb.AppendLine($"* {timestamp:yyyy-MM-dd HH:mm:ss.fff}: {direction}: [System Bytes: {formatted}]");

        // Message line
        var wbitStr = wBit ? " W" : "";
        sb.AppendLine($"S{stream}F{function}{wbitStr}");

        // Look up field metadata from templates
        Dictionary<string, FieldMetadata>? fieldMeta = null;
        var matched = MessageEditor.AllMessages
            .FirstOrDefault(m => m.Stream == stream && m.Function == function);
        if (matched?.FieldMetadataCache != null)
            fieldMeta = matched.FieldMetadataCache;

        // Tree
        if (rootItem != null)
        {
            AppendSecsItem(sb, rootItem, 0, "", fieldMeta);
            sb.AppendLine(">.");
        }
        else
        {
            sb.AppendLine("(no data)");
            sb.AppendLine(">.");
        }

        return sb.ToString();
    }

    private static void AppendSecsItem(System.Text.StringBuilder sb, SecsItem item, int indent,
        string path, Dictionary<string, FieldMetadata>? metadata)
    {
        var indentStr = new string('\t', indent);

        if (item is SecsList list)
        {
            var lengthBytes = list.Items.Length <= 0xFF ? 1 : (list.Items.Length <= 0xFFFF ? 2 : 3);
            var suffix = BuildMetaSuffix(path, null, metadata);
            sb.AppendLine($"{indentStr}<L [{list.Items.Length}/{lengthBytes}]>{suffix}");

            for (int i = 0; i < list.Items.Length; i++)
            {
                var childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
                AppendSecsItem(sb, list.Items[i], indent + 1, childPath, metadata);
            }

            sb.AppendLine($"{indentStr}>");
        }
        else
        {
            var valueStr = GetSecsValueString(item);
            var formatAbbrev = GetSecsFormatAbbrev(item.Format);
            int byteLength = GetSecsByteLength(item);
            var lengthBytes = byteLength <= 0xFF ? 1 : (byteLength <= 0xFFFF ? 2 : 3);
            var suffix = BuildMetaSuffix(path, valueStr, metadata);

            sb.AppendLine($"{indentStr}<{formatAbbrev} [{byteLength}/{lengthBytes}] {valueStr}>{suffix}");
        }
    }

    private static string GetSecsFormatAbbrev(SecsFormat format) => format switch
    {
        SecsFormat.List => "L",
        SecsFormat.ASCII => "A",
        SecsFormat.Binary => "B",
        SecsFormat.Boolean => "Boolean",
        SecsFormat.U1 => "U1", SecsFormat.U2 => "U2", SecsFormat.U4 => "U4", SecsFormat.U8 => "U8",
        SecsFormat.I1 => "I1", SecsFormat.I2 => "I2", SecsFormat.I4 => "I4", SecsFormat.I8 => "I8",
        SecsFormat.F4 => "F4", SecsFormat.F8 => "F8",
        _ => "?"
    };

    private static int GetSecsByteLength(SecsItem item) => item switch
    {
        SecsAscii a => a.Value.Length,
        SecsBinary b => b.Value.Length,
        SecsBoolean => 1,
        SecsU1 u1 => u1.Value.Length,
        SecsU2 u2 => u2.Value.Length * 2,
        SecsU4 u4 => u4.Value.Length * 4,
        SecsU8 u8 => u8.Value.Length * 8,
        SecsI1 i1 => i1.Value.Length,
        SecsI2 i2 => i2.Value.Length * 2,
        SecsI4 i4 => i4.Value.Length * 4,
        SecsI8 i8 => i8.Value.Length * 8,
        SecsF4 f4 => f4.Value.Length * 4,
        SecsF8 f8 => f8.Value.Length * 8,
        _ => 0
    };

    private static string GetSecsValueString(SecsItem item) => item switch
    {
        SecsAscii a => $"'{a.Value}'",
        SecsBinary b => string.Join(" ", b.Value.Select(bt => $"{bt:X2}")),
        SecsBoolean bo => bo.Value ? "True" : "False",
        SecsU1 u1 => u1.Value.Length == 1 ? u1.Value[0].ToString() : $"[{string.Join(", ", u1.Value)}]",
        SecsU2 u2 => u2.Value.Length == 1 ? u2.Value[0].ToString() : $"[{string.Join(", ", u2.Value)}]",
        SecsU4 u4 => u4.Value.Length == 1 ? u4.Value[0].ToString() : $"[{string.Join(", ", u4.Value)}]",
        SecsU8 u8 => u8.Value.Length == 1 ? u8.Value[0].ToString() : $"[{string.Join(", ", u8.Value)}]",
        SecsI1 i1 => i1.Value.Length == 1 ? i1.Value[0].ToString() : $"[{string.Join(", ", i1.Value)}]",
        SecsI2 i2 => i2.Value.Length == 1 ? i2.Value[0].ToString() : $"[{string.Join(", ", i2.Value)}]",
        SecsI4 i4 => i4.Value.Length == 1 ? i4.Value[0].ToString() : $"[{string.Join(", ", i4.Value)}]",
        SecsI8 i8 => i8.Value.Length == 1 ? i8.Value[0].ToString() : $"[{string.Join(", ", i8.Value)}]",
        SecsF4 f4 => f4.Value.Length == 1 ? f4.Value[0].ToString() : $"[{string.Join(", ", f4.Value)}]",
        SecsF8 f8 => f8.Value.Length == 1 ? f8.Value[0].ToString() : $"[{string.Join(", ", f8.Value)}]",
        _ => item.ToString()
    };

    private static string BuildMetaSuffix(string path, string? valueStr,
        Dictionary<string, FieldMetadata>? metadata)
    {
        if (metadata == null || string.IsNullOrEmpty(path))
            return string.Empty;

        if (!metadata.TryGetValue(path, out var meta))
            return string.Empty;

        var parts = new List<string>();

        if (!string.IsNullOrEmpty(meta.Alias))
            parts.Add(meta.Alias);

        if (meta.ValueMappings != null && !string.IsNullOrEmpty(valueStr))
        {
            var raw = valueStr.Trim('\'', '"');
            if (meta.ValueMappings.TryGetValue(raw, out var mapped))
                parts.Add($"= {mapped}");
        }

        if (!string.IsNullOrEmpty(meta.Description) && meta.Description.Length <= 40)
            parts.Add($"({meta.Description})");

        return parts.Count > 0 ? $" -- {string.Join(" ", parts)}" : string.Empty;
    }

    private void OnProtocolStateChanged(object? sender, ProtocolStateEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            LogSystem($"State changed: {e.OldState} -> {e.NewState}" +
                      (e.Reason != null ? $" ({e.Reason})" : ""));

            if (e.NewState == ProtocolState.Connected || e.NewState == ProtocolState.Online)
            {
                if (IsListening)
                {
                    // Peer connected while we were listening
                    IsListening = false;
                    IsConnected = true;
                    StatusPanel.UpdateConnectionState(true);
                    StatusText = "Peer connected! Communication established.";
                    LogSystem("Peer connected! HSMS selected. Communication established.");
                    UpdateButtonText();
                }
                else
                {
                    IsConnected = true;
                    StatusPanel.UpdateConnectionState(true);
                    StatusText = $"Connected ({e.OldState} -> {e.NewState})";
                }
            }
            else if (e.NewState == ProtocolState.Disconnected)
            {
                IsConnected = false;
                StatusPanel.UpdateConnectionState(false);

                // In Passive/Alternating mode, listener is still running — go back to listening state
                if (Config.ConnectionMode == "Passive" || Config.ConnectionMode == "Alternating")
                {
                    IsListening = true;
                    var listenAddr = Config.ConnectionMode == "Passive"
                        ? $"{Config.LocalHost}:{Config.LocalPort}"
                        : $"{Config.LocalHost}:{Config.LocalPort}";
                    StatusText = $"Peer disconnected. Listening on {listenAddr}...";
                    LogSystem($"Peer disconnected. Still listening on {listenAddr}, waiting for new connection.");
                }
                else
                {
                    IsListening = false;
                    StatusText = "Disconnected";
                    LogSystem("Connection closed.");
                }

                UpdateButtonText();
            }
            else
            {
                StatusText = $"State: {e.OldState} -> {e.NewState}";
            }
        });
    }

    private async Task HandleGemStateChangeAsync(string targetState)
    {
        if (_currentProtocol is not SecsGemProtocol secsGem)
        {
            LogSystem("ERROR: 当前协议不是 SECS/GEM，无法切换 GEM 状态。");
            return;
        }

        if (!secsGem.IsConnected())
        {
            LogSystem("ERROR: 未连接，无法切换 GEM 状态。");
            return;
        }

        var model = secsGem.EquipmentModel;
        var currentState = model.GemStateMachine.State;
        LogSystem($"GEM 状态切换请求: {currentState} -> {targetState}");

        try
        {
            switch (targetState)
            {
                case "OnlineLocal":
                    if (currentState == GemState.OnlineRemote)
                    {
                        await SendSecsTemplateAsync(secsGem, "S1F15 - Request OFF-LINE");
                        model.GemStateMachine.TryTrigger(GemEvent.GoOffline);
                        await SendSecsTemplateAsync(secsGem, "S1F17 - Request ON-LINE");
                        model.GemStateMachine.TryTrigger(GemEvent.StartCommunication);
                        model.GemStateMachine.TryTrigger(GemEvent.CommunicationEstablished);
                    }
                    else if (currentState == GemState.Offline || currentState == GemState.AttemptOnline)
                    {
                        await SendSecsTemplateAsync(secsGem, "S1F17 - Request ON-LINE");
                        model.GemStateMachine.TryTrigger(GemEvent.StartCommunication);
                        model.GemStateMachine.TryTrigger(GemEvent.CommunicationEstablished);
                    }
                    break;

                case "OnlineRemote":
                    if (currentState == GemState.OnlineLocal)
                    {
                        model.GemStateMachine.TryTrigger(GemEvent.SwitchToRemote);
                    }
                    else if (currentState == GemState.Offline || currentState == GemState.AttemptOnline)
                    {
                        await SendSecsTemplateAsync(secsGem, "S1F17 - Request ON-LINE");
                        model.GemStateMachine.TryTrigger(GemEvent.StartCommunication);
                        model.GemStateMachine.TryTrigger(GemEvent.CommunicationEstablished);
                        model.GemStateMachine.TryTrigger(GemEvent.SwitchToRemote);
                    }
                    break;

                case "Offline":
                    if (currentState == GemState.OnlineLocal || currentState == GemState.OnlineRemote)
                    {
                        await SendSecsTemplateAsync(secsGem, "S1F15 - Request OFF-LINE");
                        model.GemStateMachine.TryTrigger(GemEvent.GoOffline);
                    }
                    break;
            }

            // Update CNTRS and send S6F11 ControlStateChange event
            var newState = model.GemStateMachine.State;
            model.UpdateCntrs(newState);
            await SendControlStateChangeAsync(secsGem, model);

            var cntrsDesc = EquipmentModel.GetCntrsDescription(model.Cntrs);
            LogSystem($"GEM 状态已切换: -> {newState} (CNTRS={model.Cntrs}: {cntrsDesc})");

            UpdateStatusPanelFromModel(model);
        }
        catch (Exception ex)
        {
            LogSystem($"ERROR 切换 GEM 状态: {ex.Message}");
        }
    }

    /// <summary>
    /// Send S6F11 ControlStateChange collection event to Host.
    /// </summary>
    private async Task SendControlStateChangeAsync(SecsGemProtocol secsGem, EquipmentModel model)
    {
        // Build S6F11 with CEID=201 (ControlStateChange) and CNTRS VID
        var s6f11 = new SecsMessage(6, 11, true,
            SecsItem.L(
                SecsItem.U4(0),           // DATAID
                SecsItem.U2(201),         // CEID = ControlStateChange
                SecsItem.L(               // RPT list
                    SecsItem.L(           // Report
                        SecsItem.L(       // VID/V value pair
                            SecsItem.U2(1004),      // VID = CNTRS
                            SecsItem.A(model.Cntrs.ToString())  // Value
                        )
                    )
                )
            ));

        await secsGem.SendSecsMessageAsync(s6f11, CancellationToken.None);
    }

    private async Task SendSecsTemplateAsync(SecsGemProtocol secsGem, string templateName)
    {
        var msgVm = MessageEditor.AllMessages
            .FirstOrDefault(m => m.Name == templateName);

        if (msgVm == null)
        {
            LogSystem($"WARNING: 未找到消息模板 '{templateName}'，可用模板: {string.Join(", ", MessageEditor.AllMessages.Take(5).Select(m => m.Name))}");
            return;
        }

        var msg = msgVm.ToSecsMessage();
        await secsGem.SendSecsMessageAsync(msg, CancellationToken.None);
    }

    private void UpdateStatusPanelFromModel(EquipmentModel model)
    {
        StatusPanel.StatusVariables.Clear();
        foreach (var sv in model.StatusVariables)
        {
            StatusPanel.StatusVariables.Add(new StatusVariableViewModel
            {
                Svid = sv.Svid,
                Name = sv.Name,
                Value = sv.Value,
                Unit = sv.Unit,
            });
        }

        StatusPanel.Alarms.Clear();
        foreach (var alarm in model.Alarms)
        {
            StatusPanel.Alarms.Add(new AlarmViewModel
            {
                Alid = alarm.Alid,
                Name = alarm.Name,
                IsSet = alarm.IsSet,
            });
        }

        StatusPanel.CollectionEvents.Clear();
        foreach (var evt in model.CollectionEvents)
        {
            StatusPanel.CollectionEvents.Add(new CollectionEventViewModel
            {
                Ceid = evt.Ceid,
                Name = evt.Name,
                Enabled = evt.Enabled,
            });
        }

        StatusPanel.UpdateGemState(model.GemStateMachine.State.ToString());
    }

    private static string FormatMessageDetail(ProtocolMessage msg)
    {
        var lines = new List<string>
        {
            $"ID: {msg.Id}",
            $"Name: {msg.Name}",
            $"Description: {msg.Description}",
            "",
            "Fields:",
        };

        foreach (var kv in msg.Fields)
        {
            var val = kv.Value switch
            {
                SecsItem item => item.ToString(),
                _ => kv.Value?.ToString() ?? "(null)"
            };
            lines.Add($"  {kv.Key} = {val}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
