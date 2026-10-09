using Wandur.Desktop.Services;
using Wandur.Core.Agents;
using System.Net.Http;
using Wandur.Core.Scripting;
using L = Wandur.Core.Localization.Strings;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Wandur.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Wandur.Desktop.Security;

namespace Wandur.Desktop;

public partial class App : Application
{
    public static string? DataDirectory { get; set; }
    private ServiceProvider? _services;
    public override void Initialize()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            var directory = DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wandur");
            _services = BuildServices(directory);
            Wandur.Core.Localization.UiLanguage.Apply(_services.GetRequiredService<ISettingsStore>().Load().Settings.Language);
        }
        ConfigureDocking();
        AvaloniaXamlLoader.Load(this);
        ThemeService.Apply(new());
        WorkspaceFactory.RegisterTemplates(DataTemplates);
    }

    /// <summary>
    /// Dragging a panel shows where it will land (Styles/DockDrop.axaml). Dock's drag preview window is sized to the
    /// panel so it can show the floating window a release would make, at full opacity because the template draws its
    /// own translucency; a drop on a window-edge guide takes <see cref="DockDropPalette.WindowEdgeProportion"/> of the
    /// window. Dock reads these when it builds its templates and states, so they are set before the styles load.
    /// </summary>
    internal static void ConfigureDocking()
    {
        Dock.Settings.DockSettings.ShowDockablePreviewOnDrag = true;
        Dock.Settings.DockSettings.DragPreviewOpacity = 1;
        Dock.Settings.DockSettings.GlobalDockingProportion = DockDropPalette.WindowEdgeProportion;
    }

    private async void AboutClicked(object? sender, EventArgs args)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            await window.ShowInformationAsync(L.AboutWandur, Wandur.Core.Protocol.ClientIdentity.DisplayName, L.ADoorwayToOtherWorldsAnOpenSourceMUD);
    }

    private async void CheckForUpdatesClicked(object? sender, EventArgs args)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            await window.CheckForUpdatesFromMenuAsync();
    }

    private async void PreferencesClicked(object? sender, EventArgs args)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            await window.PreferencesAsync();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var directory = DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wandur");
            var provider = _services ??= BuildServices(directory);
            desktop.Exit += (_, _) => provider.Dispose();
            var window = provider.GetRequiredService<MainWindow>();
            desktop.MainWindow = window;
            // Off unless WANDUR_PERF_PROBE is set: the performance harness's startup and scenario probe.
            PerfProbe.Attach(window, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static ServiceProvider BuildServices(string directory)
    {
        var services = new ServiceCollection();
        services.AddClientStorage(directory);
        services.AddSingleton<IPasswordVault>(_ => OperatingSystem.IsMacOS() ? new MacPasswordVault() :
            OperatingSystem.IsWindows() ? new WindowsPasswordVault() : new LinuxPasswordVault());
        services.AddSingleton<IScriptRuntimeFactory>(_ => new ProcessScriptRuntimeFactory(
            Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path"),
            string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? typeof(Program).Assembly.Location : null));
        services.AddSingleton<Wandur.Desktop.Terminal.ITranscriptDisplayFactory, Wandur.Desktop.Terminal.TranscriptDisplayFactory>();
        services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IAgentModelProvider, OpenAiCompatibleProvider>();
        services.AddSingleton<IAgentModelProvider, LmStudioNativeProvider>();
        services.AddSingleton<IAgentProviderResolver, AgentProviderRegistry>();
        services.AddSingleton<IAgentClientServices, AgentClientServices>();
        services.AddSingleton<IProfileAutomationFactory, ProfileAutomationFactory>();
        services.AddSingleton(provider => new Wandur.Core.Classification.RoomClassificationService(directory, provider.GetRequiredService<HttpClient>()));
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

}
