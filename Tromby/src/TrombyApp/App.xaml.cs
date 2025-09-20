using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using System;
using Tromby.Core.Orchestration;
using Tromby.Core.Persona;
using Tromby.Core.Scheduling;
using Tromby.Core.Sessions;
using Tromby.Infra.Config;
using Tromby.Infra.Logging;
using Tromby.Infra.Metrics;
using Tromby.LLM.Abstractions;
using Tromby.LLM.Adapters;
using Tromby.LLM.Router;
using Tromby.Security;
using Tromby.Tools.Mcp;
using Tromby.Tools.Sandbox;
using Tromby.Voice;

namespace Tromby;

/// <summary>
/// Application bootstrapper responsible for setting up dependency injection and primary window.
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes the application and composes dependencies.
    /// </summary>
    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        ConfigureAppConfiguration(services);
        ConfigureLogging(services);
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = _serviceProvider.GetRequiredService<UI.Views.ShellWindow>();
        window.Activate();
    }

    private static void ConfigureAppConfiguration(IServiceCollection services)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("config/appsettings.json", optional: true)
            .AddJsonFile("config/budgets.json", optional: false)
            .AddJsonFile("config/persona.json", optional: false)
            .AddYamlFile("config/policy.yaml", optional: false);

        services.AddSingleton<IConfiguration>(builder.Build());
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new OverlayLoggerProvider());
        });
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigurationAdapter, ConfigurationAdapter>();
        services.AddSingleton<IAppProfileLoader, Infra.Profiles.AppProfileLoader>();
        services.AddSingleton<IMetricsCollector, MetricsCollector>();
        services.AddSingleton<LLMProviderRouter>();
        services.AddSingleton<IConsentService, ConsentService>();
        services.AddSingleton<ISessionStore, DpapiSessionStore>();
        services.AddSingleton<IPersonaManager, PersonaManager>();
        services.AddSingleton<IScheduler, AdaptiveScheduler>();
        services.AddSingleton<IOverlayOrchestrator, OverlayOrchestrator>();
        services.AddSingleton<IWhisperService, WhisperService>();
        services.AddSingleton<IEdgeTtsService, EdgeTtsService>();
        services.AddSingleton<ISapiFallbackService, SapiFallbackService>();
        services.AddSingleton<IShellSandbox, PowerShellSandbox>();
        services.AddSingleton<IMcpClient, McpClient>();
        services.AddSingleton<Security.PolicyManager>();

        services.AddHttpClient<OpenAiClient>();
        services.AddTransient<ILLMClient>(sp => sp.GetRequiredService<OpenAiClient>());
        services.AddSingleton<ILLMClient, MistralClient>();
        services.AddSingleton<ILLMClient, GeminiClient>();
        services.AddSingleton<ILLMClient, XaiClient>();
        services.AddSingleton<ILLMClient, LocalProviderClient>();

        services.AddSingleton<UI.ViewModels.ShellViewModel>();
        services.AddSingleton<UI.ViewModels.SettingsViewModel>();
        services.AddSingleton<UI.Views.ShellWindow>();
    }
}
