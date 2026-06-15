using EAPSimulator.Server;
using Serilog;
using Serilog.Events;

// Resolve config path from command line or default
var configPath = args.Length > 0 ? args[0] : "server_config.json";
if (!Path.IsPathRooted(configPath))
    configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configPath);

if (!File.Exists(configPath))
{
    // Try project root
    var projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
    var projectRootConfig = Path.Combine(projectRoot, configPath);
    if (File.Exists(projectRootConfig))
        configPath = projectRootConfig;
}

if (!File.Exists(configPath))
{
    Console.WriteLine($"Config file not found: {configPath}");
    Console.WriteLine("Usage: EAPSimulator.Server [server_config.json]");
    Console.WriteLine("Place server_config.json next to the executable or specify the path.");
    return;
}

Console.WriteLine($"Loading config from: {configPath}");
var serverConfig = ServerConfig.LoadFromFile(configPath);

// Configure Serilog
var logConfig = new LoggerConfiguration()
    .MinimumLevel.Is(Enum.Parse<LogEventLevel>(serverConfig.Logging.MinimumLevel, ignoreCase: true));

if (serverConfig.Logging.Console)
    logConfig = logConfig.WriteTo.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

if (serverConfig.Logging.File)
    logConfig = logConfig.WriteTo.File(
        serverConfig.Logging.FilePath,
        rollingInterval: Enum.Parse<RollingInterval>(serverConfig.Logging.RollingInterval, ignoreCase: true),
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

Log.Logger = logConfig.CreateLogger();

try
{
    var builder = Host.CreateDefaultBuilder(args)
        .UseWindowsService()  // Allows running as Windows Service
        .UseSerilog()
        .ConfigureServices(services =>
        {
            services.AddSingleton(serverConfig);
            services.AddHostedService<EapServerWorker>();
        });

    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "EAP Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
