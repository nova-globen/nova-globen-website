using Microsoft.Extensions.DependencyInjection;

namespace Delato.Client.Services;

/// <summary>Stomme discovers IClientStartup implementations automatically.</summary>
public sealed class ClientStartup : Stomme.Services.IClientStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        if (!services.Any(s => s.ServiceType == typeof(DelatoClient)))
        {
            services.AddScoped<DelatoClient>();
        }
    }
}
