using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Iryomera.ModIo;

public static class DependencyInjection
{
    /// <summary>Registers isolated credential-free downloads and non-logging API transport. Supply credentials explicitly.</summary>
    public static IServiceCollection AddIryomeraModIo(this IServiceCollection services, ModIoOptions options)
    {
        options.Validate(); services.AddSingleton(options); services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryModIoCredentials>(); services.TryAddSingleton<IModIoCredentials>(sp => sp.GetRequiredService<InMemoryModIoCredentials>());
        static SocketsHttpHandler Handler() => new() { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
        services.AddHttpClient("Iryomera.ModIo.Api", http => http.Timeout = TimeSpan.FromMinutes(2)).ConfigurePrimaryHttpMessageHandler(Handler).RemoveAllLoggers();
        services.AddHttpClient("Iryomera.ModIo.Download", http => http.Timeout = TimeSpan.FromMinutes(10)).ConfigurePrimaryHttpMessageHandler(Handler).RemoveAllLoggers();
        services.AddSingleton<IModIoClient>(sp => new ModIoClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Iryomera.ModIo.Api"), options, sp.GetRequiredService<IModIoCredentials>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new ModDownloadManager(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Iryomera.ModIo.Download"), options, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ModDependencyResolver>(); services.AddSingleton<ModUpdateService>(); services.AddSingleton<ModIoSource>();
        return services;
    }
}
