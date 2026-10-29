using Stomme.FeatureManagement.Application;
using Stomme.Infrastructure;

namespace Delato.Server.Services;

/// <summary>
/// Whether Delato is on for this host. Unlike the host's own reading (where an absent key means on), Delato is
/// <b>off unless configuration says <c>module.delato.enabled=true</c></b>: a host that says nothing - production -
/// shows nothing of it. The host still runs the module's startup, so its controllers, its site reconciler and its
/// guard exist; each of them asks this and stands down. Delato has no hosted service and no endpoint contributor, so
/// nothing of it depends on the host's own reading.
/// </summary>
public sealed class DelatoSwitch
{
    /// <summary>The key that turns Delato on: <c>module.delato.enabled</c>.</summary>
    public const string Key = ModuleFeatureFlags.KeyPrefix + "delato" + ModuleFeatureFlags.KeySuffix;

    public DelatoSwitch(IFeatureConfigurationSource source) =>
        IsOff = source.GetConfiguredState(Key) != true;

    internal DelatoSwitch(bool isOff) => IsOff = isOff;

    public bool IsOff { get; }
}
