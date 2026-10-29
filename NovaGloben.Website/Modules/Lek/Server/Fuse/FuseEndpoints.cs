using Stomme.Transport;
using Microsoft.AspNetCore.Routing;

namespace Lek.Server.Fuse
{
    /// <summary>
    /// Maps the Fuse hub. Stomme calls every IModuleEndpointContributor once when a transport is enabled; the hub is
    /// mapped only when SignalR is on (Transport:SignalR:Enabled), because AddSignalR runs only then. The hub takes
    /// anonymous connections on purpose: guests play with a nickname, signed-in users with their display name.
    /// </summary>
    public sealed class FuseEndpoints : IModuleEndpointContributor
    {
        public const string HubRoute = "/hubs/fuse";

        public void MapEndpoints(IEndpointRouteBuilder endpoints, TransportOptions transports)
        {
            if (transports.SignalR.Enabled)
            {
                endpoints.MapHub<FuseHub>(HubRoute);
            }
        }
    }
}
