using EAPSimulator.Core.Configuration;
using EAPSimulator.Core.EquipmentState;
using EAPSimulator.Core.Protocols;
using EAPSimulator.Core.Protocols.Bridge;
using EAPSimulator.Core.Protocols.HostProtocol;
using EAPSimulator.Core.Protocols.SecsGem;
using EAPSimulator.Core.Protocols.SecsGem.AutoReply;
using EAPSimulator.Core.Protocols.SecsGem.Gem;
using EAPSimulator.Core.Protocols.SecsGem.Hsms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Server;

/// <summary>
/// Headless EAP server that runs SECS/GEM + Host protocols with scenario engine.
/// Loads config from JSON files, no UI required.
/// </summary>
public class EapServerWorker : BackgroundService
{
    private readonly ServerConfig _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EapServerWorker> _logger;

    private SecsGemProtocol? _secsProtocol;
    private HostProtocol? _hostProtocol;
    private EapBridge? _bridge;
    private ScenarioEngine? _scenarioEngine;
    private EquipmentModel? _equipmentModel;
    private List<SecsMessageTemplate> _secsTemplateList = [];

    public EapServerWorker(ServerConfig config, ILoggerFactory loggerFactory)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<EapServerWorker>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("=== EAP Server Starting ===");

            // Load message templates
            _secsTemplateList = LoadSecsTemplates();
            var hostTemplates = LoadHostTemplates();
            var autoReplyConfig = LoadAutoReplyConfig();

            // Start SECS/GEM protocol
            await StartSecsGemAsync(_secsTemplateList, autoReplyConfig, stoppingToken);

            // Start Host protocol (if enabled)
            if (_config.Host.Enabled)
            {
                await StartHostAsync(hostTemplates, stoppingToken);
            }

            _logger.LogInformation("=== EAP Server Running ===");
            _logger.LogInformation("SECS/GEM: {Mode} on {LocalHost}:{LocalPort}",
                _config.SecsGem.ConnectionMode, _config.SecsGem.LocalHost, _config.SecsGem.LocalPort);
            if (_config.Host.Enabled)
                _logger.LogInformation("Host: {Transport} ({Mode})", _config.Host.TransportType,
                    _config.Host.IsActiveMode ? "Active" : "Passive");

            // Keep running until cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) { }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "EAP Server fatal error");
        }
        finally
        {
            _logger.LogInformation("=== EAP Server Stopping ===");
            await CleanupAsync();
        }
    }

    private List<SecsMessageTemplate> LoadSecsTemplates()
    {
        var path = ResolvePath(_config.Files.SecsMessageTemplates);
        if (!File.Exists(path))
        {
            _logger.LogError("SECS message templates file not found: {Path}", path);
            return [];
        }

        var file = SecsMessageTemplateFile.LoadFromFile(path);
        _logger.LogInformation("Loaded {Count} SECS message templates from {Path}", file.Messages.Count, path);
        return file.Messages;
    }

    private List<HostMessageTemplate> LoadHostTemplates()
    {
        var path = ResolvePath(_config.Files.HostMessageTemplates);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Host message templates file not found: {Path}", path);
            return [];
        }

        var collection = HostMessageTemplateCollection.LoadFromFile(path);
        _logger.LogInformation("Loaded {Count} Host message templates from {Path}", collection.Templates.Count, path);
        return collection.Templates;
    }

    private AutoReplyConfig LoadAutoReplyConfig()
    {
        var path = ResolvePath(_config.Files.AutoReplyRules);
        var config = AutoReplyConfig.LoadFromFile(path);
        _logger.LogInformation("Loaded {QuickCount} quick-reply rules, {ScenarioCount} scenarios from {Path}",
            config.QuickReplies.Count, config.Scenarios.Count, path);
        return config;
    }

    private async Task StartSecsGemAsync(List<SecsMessageTemplate> templates,
        AutoReplyConfig autoReplyConfig, CancellationToken ct)
    {
        var secsConfig = _config.SecsGem;
        var settings = new HsmsSettings
        {
            ConnectionMode = secsConfig.ConnectionMode switch
            {
                "Active" => ConnectionMode.Active,
                "Alternating" => ConnectionMode.Alternating,
                _ => ConnectionMode.Passive
            },
            RemoteHost = secsConfig.RemoteHost,
            RemotePort = secsConfig.RemotePort,
            LocalHost = secsConfig.LocalHost,
            LocalPort = secsConfig.LocalPort,
            DeviceId = (ushort)secsConfig.DeviceId,
            T3Timeout = secsConfig.T3Timeout,
            T5Timeout = secsConfig.T5Timeout,
            T6Timeout = secsConfig.T6Timeout,
            T7Timeout = secsConfig.T7Timeout,
            T8Timeout = secsConfig.T8Timeout,
        };

        var equipModel = new EquipmentModel
        {
            AcceptCommunication = secsConfig.AcceptCommunication
        };
        _equipmentModel = equipModel;

        _secsProtocol = new SecsGemProtocol(
            _loggerFactory.CreateLogger<SecsGemProtocol>(),
            _loggerFactory.CreateLogger<HsmsTransport>(),
            _loggerFactory.CreateLogger<MessageRouter>(),
            settings,
            equipModel);

        _secsProtocol.MessageReceived += (_, e) =>
        {
            var stream = e.Message.GetField<byte>("Stream");
            var function = e.Message.GetField<byte>("Function");
            var wBit = e.Message.GetField<bool>("WBit");
            _logger.LogInformation("[SECS <<] S{Stream}F{Function}{WBit}",
                stream, function, wBit ? " W" : "");
        };

        _secsProtocol.MessageSent += (_, e) =>
        {
            var stream = e.Message.GetField<byte>("Stream");
            var function = e.Message.GetField<byte>("Function");
            var wBit = e.Message.GetField<bool>("WBit");
            _logger.LogInformation("[SECS >>] S{Stream}F{Function}{WBit}",
                stream, function, wBit ? " W" : "");
        };

        _secsProtocol.StateChanged += (_, e) =>
        {
            _logger.LogInformation("[SECS State] {Old} -> {New}", e.OldState, e.NewState);
        };

        // Build template lookup
        var templateDict = templates.ToDictionary(t => t.Name, t => t);
        Func<string, SecsMessageTemplate?> templateLookup = name =>
            templateDict.TryGetValue(name, out var t) ? t : null;

        // Register quick-reply rules
        foreach (var rule in autoReplyConfig.QuickReplies.Where(r => r.Enabled))
        {
            var template = templateLookup(rule.ReplyTemplateName);
            if (template == null)
            {
                _logger.LogWarning("Quick-reply template not found: {Name}", rule.ReplyTemplateName);
                continue;
            }
            _secsProtocol.Router.RegisterQuickReplyRule(rule, template);
        }

        // Setup scenario engine
        var enabledScenarios = autoReplyConfig.Scenarios.Where(s => s.Enabled).ToList();
        if (enabledScenarios.Count > 0)
        {
            _scenarioEngine = new ScenarioEngine(
                _loggerFactory.CreateLogger<ScenarioEngine>(),
                enabledScenarios,
                templateLookup,
                stateAlterHandler: (varName, value) =>
                {
                    if (_equipmentModel == null) return;
                    var sv = _equipmentModel.StatusVariables.FirstOrDefault(v => v.Name == varName);
                    if (sv != null)
                    {
                        sv.Value = value;
                        _logger.LogInformation("State alter: {Var} = {Value}", varName, value);
                    }
                    else
                    {
                        _logger.LogWarning("State alter: variable '{Var}' not found", varName);
                    }
                });
            _secsProtocol.Router.SetScenarioEngine(_scenarioEngine);
            _logger.LogInformation("Scenario engine loaded with {Count} scenarios", enabledScenarios.Count);
        }

        await _secsProtocol.StartAsync(ct);
    }

    private async Task StartHostAsync(List<HostMessageTemplate> hostTemplates, CancellationToken ct)
    {
        var hostConfig = _config.Host;
        var transportType = Enum.Parse<TransportType>(hostConfig.TransportType, ignoreCase: true);

        var transportConfig = new HostTransportConfig
        {
            TransportType = transportType,
            IsActiveMode = hostConfig.IsActiveMode,
            RemoteHost = hostConfig.RemoteHost,
            RemotePort = hostConfig.RemotePort,
            LocalHost = hostConfig.LocalHost,
            LocalPort = hostConfig.LocalPort,
            HttpUrl = hostConfig.HttpUrl,
            HttpHeaders = hostConfig.HttpHeaders,
            BrokerUri = hostConfig.BrokerUri,
            QueueName = hostConfig.QueueName,
            ResponseQueueName = hostConfig.ResponseQueueName,
            Username = hostConfig.Username,
            Password = hostConfig.Password,
            RabbitMqHost = hostConfig.RabbitMqHost,
            RabbitMqPort = hostConfig.RabbitMqPort,
            RabbitMqExchange = hostConfig.RabbitMqExchange,
            RabbitMqRoutingKey = hostConfig.RabbitMqRoutingKey,
            RabbitMqQueue = hostConfig.RabbitMqQueue,
            KafkaBootstrapServers = hostConfig.KafkaBootstrapServers,
            KafkaTopic = hostConfig.KafkaTopic,
            KafkaGroupId = hostConfig.KafkaGroupId,
            GrpcEndpoint = hostConfig.GrpcEndpoint,
            MqttBroker = hostConfig.MqttBroker,
            MqttPort = hostConfig.MqttPort,
            MqttTopic = hostConfig.MqttTopic,
            MqttClientId = hostConfig.MqttClientId,
        };

        _hostProtocol = new HostProtocol(_loggerFactory.CreateLogger<HostProtocol>());
        _hostProtocol.Configure(transportConfig);

        _hostProtocol.MessageReceived += (_, e) =>
        {
            _logger.LogInformation("[Host <<] {Name}", e.Message.Name);
            // Forward to scenario engine for Host trigger matching
            if (_scenarioEngine != null)
            {
                var fields = e.Message.Fields
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!.ToString() ?? "");
                _scenarioEngine.HandleHostMessage(e.Message.Name, fields);
            }
        };

        _hostProtocol.MessageSent += (_, e) =>
            _logger.LogInformation("[Host >>] {Name}", e.Message.Name);

        _hostProtocol.StateChanged += (_, e) =>
            _logger.LogInformation("[Host State] {Old} -> {New}", e.OldState, e.NewState);

        await _hostProtocol.StartAsync(ct);

        // Wire up bridge if SECS is also connected
        if (_secsProtocol != null)
        {
            var stateManager = EquipmentStateManager.CreateDefault();
            _bridge = new EapBridge(_loggerFactory.CreateLogger<EapBridge>(), stateManager);

            // Load templates into bridge for message building
            _bridge.SetHostTemplates(hostTemplates);
            _bridge.SetSecsTemplates(_secsTemplateList);

            // Wire bridge events for logging
            _bridge.BridgeEvent += (_, args) =>
                _logger.LogInformation("[Bridge] {Type}: {Description}", args.Type, args.Description);

            _bridge.AttachSecsProtocol(_secsProtocol);
            _bridge.AttachHostProtocol(_hostProtocol);
            _logger.LogInformation("EAP Bridge established (SECS <-> Host)");
        }

        // Wire up scenario engine host actions
        if (_scenarioEngine != null)
        {
            _scenarioEngine.HostActionTriggered += async (_, args) =>
            {
                try
                {
                    var template = hostTemplates.FirstOrDefault(t => t.Name == args.HostMessageName);
                    if (template == null)
                    {
                        _logger.LogWarning("Scenario Host action: template not found '{Name}'", args.HostMessageName);
                        return;
                    }

                    var hostMsg = template.BuildMessage();
                    await _hostProtocol.SendHostMessageAsync(hostMsg, ct);
                    _logger.LogInformation("Scenario Host action: sent '{Name}'", args.HostMessageName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scenario Host action failed");
                }
            };

            _scenarioEngine.StateAlterTriggered += (_, args) =>
                _logger.LogInformation("Scenario State alter: {Var} = {Value}", args.VariableName, args.NewValue);
        }
    }

    private async Task CleanupAsync()
    {
        if (_bridge != null)
            await _bridge.DisposeAsync();

        if (_hostProtocol != null)
        {
            await _hostProtocol.StopAsync(CancellationToken.None);
            await _hostProtocol.DisposeAsync();
        }

        if (_secsProtocol != null)
        {
            await _secsProtocol.StopAsync(CancellationToken.None);
            await _secsProtocol.DisposeAsync();
        }
    }

    private string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
    }
}
