using System.Globalization;

namespace Delato.Client.Services;

/// <summary>
/// The page asks for a rate the way people quote it - "1 USD = ? XXX" - while the API stores the inverse
/// (<c>fxRateToUsd</c>: 1 XXX = ? USD, at most 8 decimals). This is the one conversion between the two.
/// </summary>
public static class FxRate
{
    /// <summary>"1 USD = <paramref name="perUsd"/> XXX" as fxRateToUsd, half-up to 8 decimals; null when not positive.</summary>
    public static decimal? FromUsdPrice(decimal perUsd)
    {
        if (perUsd <= 0) return null;
        var rate = Math.Round(1m / perUsd, 8, MidpointRounding.AwayFromZero);
        return rate > 0 ? rate : null;
    }

    /// <summary>fxRateToUsd as "1 USD = ? XXX", for prefilling the field when editing (at most 6 decimals).</summary>
    public static decimal? ToUsdPrice(decimal fxRateToUsd)
    {
        if (fxRateToUsd <= 0) return null;
        var price = Math.Round(1m / fxRateToUsd, 6, MidpointRounding.AwayFromZero);
        return decimal.Parse(price.ToString("0.######", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
