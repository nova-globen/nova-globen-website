using System.Globalization;
using System.Text;

namespace Delato.Client.Services;

/// <summary>
/// Money as people read and type it, in both languages.
/// <list type="bullet">
/// <item><b>Showing:</b> an amount is written with the CURRENCY's own decimals (0 for IRR, IRT and JPY, else 2) and its
/// ISO code, with invariant (Latin) digits and separators - never the UI culture's, which in Persian would print rial
/// formats and Persian digits for a dollar amount.</item>
/// <item><b>Reading:</b> a typed number may use Latin, Persian (۰-۹) or Arabic-Indic (٠-٩) digits, the Persian decimal
/// separator "٫" or a point, and group separators ("," "٬" or spaces), which are dropped. It is normalised to plain
/// invariant text before <c>decimal.Parse(InvariantCulture)</c>. Signs, exponents and anything else are refused.</item>
/// </list>
/// </summary>
public static class MoneyText
{
    private static readonly HashSet<string> ZeroDecimals = new(StringComparer.Ordinal) { "IRR", "IRT", "JPY" };

    /// <summary>The decimals a currency is shown with.</summary>
    public static int DecimalsOf(string? currency) => currency is not null && ZeroDecimals.Contains(currency) ? 0 : 2;

    /// <summary>"1,234.56 USD", "5,000,000 IRR": the currency's decimals (rounded half-up for showing only), invariant digits.</summary>
    public static string Format(decimal amount, string currency)
    {
        var decimals = DecimalsOf(currency);
        var shown = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return shown.ToString("N" + decimals, CultureInfo.InvariantCulture) + " " + currency;
    }

    /// <summary>A plain number with invariant digits and no grouping ("1234.5"), for prefilling an input.</summary>
    public static string Plain(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>The typed number, or null when it is not a plain non-negative number.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = new StringBuilder(text.Length);
        var points = 0;
        foreach (var c in text.Trim())
        {
            switch (c)
            {
                case >= '0' and <= '9':
                    plain.Append(c);
                    break;
                case >= '۰' and <= '۹': // Persian (extended Arabic-Indic) digits
                    plain.Append((char)('0' + (c - '۰')));
                    break;
                case >= '٠' and <= '٩': // Arabic-Indic digits
                    plain.Append((char)('0' + (c - '٠')));
                    break;
                case '.' or '٫': // a point, or the Arabic/Persian decimal separator
                    points++;
                    plain.Append('.');
                    break;
                case ',' or '٬' or ' ' or ' ' or ' ': // group separators and spaces are dropped
                    break;
                default:
                    return null;
            }
        }
        if (points > 1 || plain.Length == 0 || plain[0] == '.' || plain[^1] == '.') return null;
        return decimal.TryParse(plain.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
