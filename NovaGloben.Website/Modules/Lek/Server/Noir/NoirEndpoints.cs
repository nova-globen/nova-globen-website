using Stomme.Transport;
using Microsoft.AspNetCore.Routing;

namespace Lek.Server.Noir
{
    /// <summary>Maps the Bullet Noir hub when SignalR is on; anonymous on purpose, like Fuse.</summary>
    public sealed class NoirEndpoints : IModuleEndpointContributor
    {
        public const string HubRoute = "/hubs/noir";

        public void MapEndpoints(IEndpointRouteBuilder endpoints, TransportOptions transports)
        {
            if (transports.SignalR.Enabled) endpoints.MapHub<NoirHub>(HubRoute);
        }
    }
}
