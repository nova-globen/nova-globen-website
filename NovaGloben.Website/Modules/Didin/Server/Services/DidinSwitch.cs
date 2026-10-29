using Stomme.FeatureManagement.Application;
using Stomme.Infrastructure;

namespace Didin.Server.Services
{
    /// <summary>
    /// Whether this host switched Didin off (<c>module.didin.enabled=false</c>), read the way the host reads it to skip a
    /// module's hosted services and endpoints. The host still runs a switched-off module's startup, so its controllers,
    /// its site reconciler and its middleware exist; each of them asks this and stands down.
    /// </summary>
    public sealed class DidinSwitch
    {
        public DidinSwitch(IFeatureConfigurationSource source) =>
            IsOff = ModuleFeatureFlags.IsSwitchedOff(source, typeof(DidinSwitch).Assembly);

        internal DidinSwitch(bool isOff) => IsOff = isOff;

        public bool IsOff { get; }
    }
}
