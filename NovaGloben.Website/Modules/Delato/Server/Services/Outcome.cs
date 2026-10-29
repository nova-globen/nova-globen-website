using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Server.Services;

/// <summary>What a ledger call answers: an HTTP status and its body (a DTO, or the error envelope).</summary>
public sealed record Outcome(int Status, object? Body)
{
    public static Outcome Ok(object body) => new(200, body);
    public static Outcome Created(object body) => new(201, body);
    public static Outcome NoContent() => new(204, null);

    public static Outcome Error(int status, string code, string message) =>
        new(status, new ErrorEnvelope(new ErrorBody(code, message)));

    public static Outcome Validation(string message) => Error(400, "VALIDATION", message);
    public static Outcome NotFound(string message = "Not found.") => Error(404, "NOT_FOUND", message);
    public static Outcome Forbidden(string message = "Forbidden.") => Error(403, "FORBIDDEN", message);
    public static Outcome Unauthorized(string message = "Unauthorized.") => Error(401, "UNAUTHORIZED", message);
    public static Outcome Conflict(string code, string message) => Error(409, code, message);

    public static Outcome FromDomain(DomainException ex) =>
        ex.Code is "SPLIT_MISMATCH" or "MEMBER_HAS_BALANCE" ? Error(409, ex.Code, ex.Message) : Error(400, ex.Code, ex.Message);

    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>The currencies an expense or payment may be entered in. USD is the settlement currency.</summary>
public static class Currencies
{
    public const string Settlement = "USD";

    public static readonly CurrencyDto[] All =
    [
        new("USD", "$", "US Dollar", "دلار آمریکا"),
        new("EUR", "€", "Euro", "یورو"),
        new("GBP", "£", "British Pound", "پوند بریتانیا"),
        new("IRR", "﷼", "Iranian Rial", "ریال ایران"),
        new("IRT", "تومان", "Iranian Toman", "تومان ایران"),
        new("TRY", "₺", "Turkish Lira", "لیر ترکیه"),
        new("AED", "د.إ", "UAE Dirham", "درهم امارات"),
        new("CAD", "$", "Canadian Dollar", "دلار کانادا"),
        new("AUD", "$", "Australian Dollar", "دلار استرالیا"),
        new("JPY", "¥", "Japanese Yen", "ین ژاپن"),
        new("CHF", "Fr", "Swiss Franc", "فرانک سوئیس"),
        new("SEK", "kr", "Swedish Krona", "کرون سوئد"),
        new("NOK", "kr", "Norwegian Krone", "کرون نروژ"),
        new("INR", "₹", "Indian Rupee", "روپیه هند"),
        new("CNY", "¥", "Chinese Yuan", "یوان چین"),
    ];

    public static bool IsValid(string? code) => code is not null && All.Any(c => c.Code == code);
}

internal static class Decimals
{
    public static bool HasMaxScale(decimal value, int maxScale) => decimal.Round(value, maxScale) == value;
}
