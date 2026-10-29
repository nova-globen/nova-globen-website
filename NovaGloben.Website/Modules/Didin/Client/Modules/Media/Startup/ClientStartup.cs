using Didin.Module.Media.Services;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace Didin.Module.Media.Startup
{
    /// <summary>Stomme discovers IClientStartup implementations automatically.</summary>
    public class ClientStartup : Stomme.Services.IClientStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddFluentUIComponents();
            if (!services.Any(s => s.ServiceType == typeof(ICatalogClient)))
            {
                services.AddScoped<ICatalogClient, CatalogClient>();
            }
            if (!services.Any(s => s.ServiceType == typeof(MediaState)))
            {
                services.AddScoped<MediaState>();
            }
            if (!services.Any(s => s.ServiceType == typeof(ICastDevice)))
            {
                // cast to TV with Google's Cast SDK in Chrome
                services.AddScoped<ICastDevice, WebCastDevice>();
            }
            if (!services.Any(s => s.ServiceType == typeof(MediaOptions)))
            {
                // same-origin + cookie auth inside the Stomme site
                services.AddScoped(_ => new MediaOptions());
            }
        }
    }
}
