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

    /// <summary>
    /// A request whose prompt - fresh input, cache writes and cache reads together - is longer than this many tokens is
    /// priced as long context, as Haiku 5.5 is past 100K: its whole answer costs <see cref="LongContextMultiplier"/>
    /// times its units.
    /// </summary>
    public long LongContextThreshold { get; set; } = 100_000;

    /// <summary>What a long-context request's units are multiplied by (Haiku 5.5's long-context price: five times).</summary>
    public double LongContextMultiplier { get; set; } = 5;

    /// <summary>
    /// The weighted units an answer costs, rounded up, so a run of tiny answers never costs nothing; times
    /// <see cref="LongContextMultiplier"/> when its prompt is longer than <see cref="LongContextThreshold"/>.
    /// </summary>
    public long Units(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var units = usage.Input * (decimal)Input
                    + usage.CacheWrite * (decimal)CacheWrite
                    + usage.CacheRead * (decimal)CacheRead
                    + usage.Output * (decimal)Output;
        if (usage.Input + usage.CacheWrite + usage.CacheRead > LongContextThreshold)
        {
            units *= (decimal)LongContextMultiplier;
        }
        return units >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(units);
    }

    /// <summary>
    /// Problems with the weights: each must be a finite number, zero or more; the long-context threshold a positive
    /// number of tokens, and its multiplier a finite number from 1 (long context never costs less).
    /// </summary>
    public IEnumerable<string> Problems()
    {
        foreach (var (name, value) in new[] { ("Input", Input), ("CacheWrite", CacheWrite), ("CacheRead", CacheRead), ("Output", Output) })
        {
            if (!double.IsFinite(value) || value < 0 || value > 1_000_000)
            {
                yield return $"Broker:TokenWeights:{name} must be a number from 0 to 1000000 - every budget is counted with it.";
            }
        }
        if (LongContextThreshold <= 0)
        {
            yield return "Broker:TokenWeights:LongContextThreshold must be a positive number of tokens - a request longer than it is priced as long context.";
        }
        if (!double.IsFinite(LongContextMultiplier) || LongContextMultiplier < 1 || LongContextMultiplier > 1000)
        {
            yield return "Broker:TokenWeights:LongContextMultiplier must be a number from 1 to 1000 - long context never costs less than short.";
        }
    }
}
