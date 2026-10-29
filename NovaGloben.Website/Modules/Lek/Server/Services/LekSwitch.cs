using Stomme.FeatureManagement.Application;
using Stomme.Infrastructure;

namespace Lek.Server.Services
{
    /// <summary>
    /// Whether this host switched Lek off (<c>module.lek.enabled=false</c>), read the way the host reads it to skip a
    /// module's hosted services and endpoints. The host still runs a switched-off module's startup, so its controllers,
    /// its site reconciler and its guards exist; each of them asks this and stands down.
    /// </summary>
    public sealed class LekSwitch
    {
        public LekSwitch(IFeatureConfigurationSource source) =>
            IsOff = ModuleFeatureFlags.IsSwitchedOff(source, typeof(LekSwitch).Assembly);

        internal LekSwitch(bool isOff) => IsOff = isOff;

        public bool IsOff { get; }
    }
}
