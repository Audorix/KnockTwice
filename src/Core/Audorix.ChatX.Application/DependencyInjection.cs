using Audorix.ChatX.Application.Installations;
using Microsoft.Extensions.DependencyInjection;

namespace Audorix.ChatX.Application;

public static class DependencyInjection
{
    /// <summary>Registers application use cases. Ports are implemented and registered by Infrastructure.</summary>
    public static IServiceCollection AddChatXApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<InstallationService>();
        return services;
    }
}
