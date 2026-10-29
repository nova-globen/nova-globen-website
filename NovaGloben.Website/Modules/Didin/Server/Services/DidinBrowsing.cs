using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace Didin.Server.Services
{
    /// <summary>
    /// Who may browse the catalogue, see its art and play its files. A signed-in account always may. Anyone may too
    /// on a host that sets <c>Didin:PublicCatalog=true</c>: production, whose catalogue is the public sample of openly
    /// licensed films. Off unless set. Everything that is someone's own - their list, progress, ratings, the
    /// assistant (it spends money), casting, the library and the import - stays signed-in only whatever this says.
    /// </summary>
    public static class DidinBrowsing
    {
        public const string Policy = "Didin.Browse";
        public const string PublicKey = "Didin:PublicCatalog";

        public static bool IsPublic(IConfiguration configuration) =>
            string.Equals(configuration[PublicKey], "true", System.StringComparison.OrdinalIgnoreCase);

        public sealed class Requirement : IAuthorizationRequirement
        {
        }

        public sealed class Handler(IConfiguration configuration) : AuthorizationHandler<Requirement>
        {
            protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, Requirement requirement)
            {
                if (context.User?.Identity?.IsAuthenticated == true || IsPublic(configuration))
                {
                    context.Succeed(requirement);
                }
                return Task.CompletedTask;
            }
        }
    }
}
