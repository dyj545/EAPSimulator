using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EAPSimulator.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EAPSimulator.UI;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _host = Host.CreateDefaultBuilder()
            .UseSerilog((ctx, lc) => lc
                .WriteTo.Console()
                .WriteTo.File("logs/eap-.log", rollingInterval: RollingInterval.Day))
            .ConfigureServices(services =>
            {
                services.AddSingleton<MainViewModel>();
                services.AddTransient<ConfigViewModel>();
                services.AddTransient<MessageLogViewModel>();
                services.AddTransient<StatusPanelViewModel>();
            })
            .Build();

        _host.Start();

        var viewModel = _host.Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.Exit += async (_, _) =>
            {
                await _host.StopAsync();
                _host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
