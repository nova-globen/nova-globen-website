namespace Fravashi.Broker.Containment;

/// <summary>Whether a request's model is one the deployment's grant covers, and why not when it is not.</summary>
/// <param name="Covered">True when the request may go upstream.</param>
/// <param name="Reason">A short, secret-free reason for the refusal and the audit; null when covered.</param>
/// <param name="Model">The model that was asked for, so a refusal can name it; null when none was readable.</param>
public sealed record ModelDecision(bool Covered, string? Reason, string? Model);

/// <summary>
/// Which models a deployment's grant covers. Until this existed the broker forwarded any model the
/// provider offers, so a grant meant "whatever is dearest". An entry is an exact model id, or a prefix ending in
/// <c>*</c> (a dated id changes with every release of a model, and an operator should not have to chase it).
/// <para>
/// With NO models named the policy is unrestricted, and says so. That is a deliberate exception to failing
/// closed, made for one reason: a broker already running with a configuration from before this setting must not
/// have a list that refuses everything until somebody fills it in, which would take its conversations down the
/// moment the image was upgraded. The start-up log says loudly that nothing is named.
/// Once ANY model is named the policy does fail closed: a request whose model cannot be read is refused.
/// </para>
/// </summary>
public sealed class ModelPolicy
{
    private readonly string[] _exact;
    private readonly string[] _prefixes;

    public ModelPolicy(IEnumerable<string>? allowed)
    {
        var entries = (allowed ?? [])
            .Select(entry => entry?.Trim() ?? string.Empty)
            .Where(entry => entry.Length > 0)
            .ToArray();
        Advertised = entries;

        _exact = [.. entries.Where(entry => !entry.EndsWith('*'))];
        _prefixes = [.. entries.Where(entry => entry.EndsWith('*')).Select(entry => entry[..^1])];
    }

    /// <summary>True when at least one model is named, so requests are held against the list.</summary>
    public bool IsRestricted => _exact.Length > 0 || _prefixes.Length > 0;

    /// <summary>
    /// The list as configured, in its order (trimmed, blanks dropped): what a client is told it may ask for, so it
    /// offers only these. An entry ending in <c>*</c> is a prefix; an empty list means any model.
    /// </summary>
    public IReadOnlyList<string> Advertised { get; }

    /// <summary>Holds a request's model against the list.</summary>
    public ModelDecision Check(string? model)
    {
        if (!IsRestricted)
        {
            return new ModelDecision(true, null, model);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return new ModelDecision(false, "model_unreadable", null);
        }

        var asked = model.Trim();
        var covered = _exact.Any(entry => string.Equals(entry, asked, StringComparison.OrdinalIgnoreCase))
            || _prefixes.Any(prefix => asked.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return covered ? new ModelDecision(true, null, asked) : new ModelDecision(false, "model_not_covered", asked);
    }
}
