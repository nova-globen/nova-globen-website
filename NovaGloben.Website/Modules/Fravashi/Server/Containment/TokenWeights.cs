namespace Fravashi.Broker.Containment;

/// <summary>What one answer used, in the provider's own four figures.</summary>
public sealed record TokenUsage(long Input, long CacheWrite, long CacheRead, long Output)
{
    /// <summary>All four together: the raw token count.</summary>
    public long Total => Input + CacheWrite + CacheRead + Output;
}

/// <summary>
/// What each kind of token costs against a budget, relative to a fresh input token. The budgets
/// (<see cref="Configuration.BrokerServiceOptions.QuotaTokens"/>, <see cref="Configuration.BrokerServiceOptions.OrganisationTokensPerDay"/>)
/// count these weighted units, not raw tokens, because the kinds are priced very differently: an agent's CLI writes
/// ~36k tokens of system prompt and tools into the cache before the user types anything, and reads them back on every
/// turn at a tenth of the price, while one output token costs five inputs on Haiku 5.5. Counted flat, a free tier
/// would run out at a few dozen conversations a day for a fraction of the money. Bound from <c>Broker:TokenWeights</c>;
/// the defaults are Haiku 5.5's price ratios.
/// </summary>
public sealed class TokenWeights
{
    public double Input { get; set; } = 1;
    public double CacheWrite { get; set; } = 1.25;
    public double CacheRead { get; set; } = 0.1;
    public double Output { get; set; } = 5;

    /// <summary>The weighted units an answer costs, rounded up, so a run of tiny answers never costs nothing.</summary>
    public long Units(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var units = usage.Input * (decimal)Input
                    + usage.CacheWrite * (decimal)CacheWrite
                    + usage.CacheRead * (decimal)CacheRead
                    + usage.Output * (decimal)Output;
        return units >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(units);
    }

    /// <summary>Problems with the weights: each must be a finite number, zero or more.</summary>
    public IEnumerable<string> Problems()
    {
        foreach (var (name, value) in new[] { ("Input", Input), ("CacheWrite", CacheWrite), ("CacheRead", CacheRead), ("Output", Output) })
        {
            if (!double.IsFinite(value) || value < 0 || value > 1_000_000)
            {
                yield return $"Broker:TokenWeights:{name} must be a number from 0 to 1000000 - every budget is counted with it.";
            }
        }
    }
}
