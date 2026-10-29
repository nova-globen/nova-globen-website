using Stomme.Transport;
using Microsoft.AspNetCore.Routing;

namespace Lek.Server.Elementals
{
    /// <summary>Maps the Elementals hub when SignalR is on; anonymous on purpose, like Fuse.</summary>
    public sealed class ElementalsEndpoints : IModuleEndpointContributor
    {
        public const string HubRoute = "/hubs/elementals";

        public void MapEndpoints(IEndpointRouteBuilder endpoints, TransportOptions transports)
        {
            if (transports.SignalR.Enabled) endpoints.MapHub<ElementalsHub>(HubRoute);
        }
    }
}
