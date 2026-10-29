using Stomme.Transport;
using Microsoft.AspNetCore.Routing;

namespace Lek.Server.RelicRush
{
    /// <summary>Maps the Relic Rush hub when SignalR is on; anonymous on purpose, like Fuse.</summary>
    public sealed class RelicEndpoints : IModuleEndpointContributor
    {
        public const string HubRoute = "/hubs/relicrush";

        public void MapEndpoints(IEndpointRouteBuilder endpoints, TransportOptions transports)
        {
            if (transports.SignalR.Enabled) endpoints.MapHub<RelicHub>(HubRoute);
        }
    }
}
