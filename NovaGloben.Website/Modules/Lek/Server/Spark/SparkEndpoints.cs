using Stomme.Transport;
using Microsoft.AspNetCore.Routing;

namespace Lek.Server.Spark
{
    /// <summary>Maps the Spark hub when SignalR is on; anonymous on purpose, like Fuse.</summary>
    public sealed class SparkEndpoints : IModuleEndpointContributor
    {
        public const string HubRoute = "/hubs/spark";

        public void MapEndpoints(IEndpointRouteBuilder endpoints, TransportOptions transports)
        {
            if (transports.SignalR.Enabled) endpoints.MapHub<SparkHub>(HubRoute);
        }
    }
}
