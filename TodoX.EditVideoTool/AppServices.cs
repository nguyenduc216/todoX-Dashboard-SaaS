using Microsoft.Extensions.DependencyInjection;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Projects;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool;

/// <summary>Everything the exe needs, for one install folder (the setup wizard builds one for the folder it installs to).</summary>
public static class AppServices
{
    public static void Add(IServiceCollection services, AgentPaths? paths = null)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        services.AddSingleton(paths ?? AgentPaths.Default());
        services.AddSingleton<ComponentLocator>();
        services.AddSingleton<LicenseStore>();
        services.AddSingleton(_ => LicenseVerifier.Default());
        services.AddSingleton<IDeviceIdentity, WindowsDeviceIdentity>();
        services.AddSingleton(_ => new LicenseApiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }));
        services.AddSingleton<LicenseGuard>(sp => new LicenseGuard(
            sp.GetRequiredService<LicenseStore>(),
            sp.GetRequiredService<LicenseVerifier>(),
            sp.GetRequiredService<IDeviceIdentity>(),
            sp.GetRequiredService<LicenseApiClient>()));
        services.AddSingleton(sp => new TodoXServerClient(http, sp.GetRequiredService<LicenseStore>(), sp.GetRequiredService<IDeviceIdentity>()));
        services.AddSingleton<INotifier, WindowsNotifier>();
        services.AddSingleton(sp => new ServerConnectivity(sp.GetRequiredService<TodoXServerClient>(), sp.GetRequiredService<INotifier>()));
        services.AddSingleton<StatusService>();
        services.AddSingleton<MediaProbe>();
        services.AddSingleton<ClipNormalizer>();
        services.AddSingleton<Transcriber>();
        services.AddSingleton<RemotionRenderer>();
        services.AddSingleton<HealthCheck>();
        services.AddSingleton<Installer>();
        services.AddSingleton<InstallLogUploader>();
        services.AddSingleton<Updater>();
        services.AddSingleton<SkillSync>();
        services.AddSingleton<ProjectFolders>();
        services.AddSingleton(sp => new AiGenerator(sp.GetRequiredService<TodoXServerClient>()));
        services.AddSingleton<EditStore>();
        services.AddSingleton<SkillRunStore>();
    }

    public static ServiceProvider Build(AgentPaths? paths = null)
    {
        var services = new ServiceCollection();
        Add(services, paths);
        return services.BuildServiceProvider();
    }
}
