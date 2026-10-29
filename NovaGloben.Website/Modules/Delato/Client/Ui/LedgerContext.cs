using Delato.Client.Services;
using Microsoft.Extensions.Localization;
using Stomme.Services;

namespace Delato.Client.Ui;

/// <summary>What every part of the ledger page shares: the API, the texts, the currencies and how a failure is told.</summary>
public sealed class LedgerContext
{
    public required DelatoClient Api { get; init; }
    public required IStringLocalizer<DelatoText> T { get; init; }
    public List<Shared.Contracts.CurrencyDto> Currencies { get; set; } = [];
    public bool IsAdministrator { get; init; }

    /// <summary>Shows a failure (the server's reason when it gave one) to the user.</summary>
    public required Action<string> ShowError { get; init; }

    /// <summary>Runs a call; on failure shows its reason and returns false.</summary>
    public async Task<bool> TryAsync(Func<Task> call)
    {
        try
        {
            await call();
            return true;
        }
        catch (ServiceRefusalException refusal)
        {
            ShowError(string.IsNullOrWhiteSpace(refusal.Message) ? T["ErrorUnknown"] : refusal.Message);
            return false;
        }
        catch (HttpRequestException)
        {
            ShowError(T["ErrorNetwork"]);
            return false;
        }
    }

    /// <summary>A currency's name in the page's language.</summary>
    public string CurrencyName(string code)
    {
        var currency = Currencies.FirstOrDefault(c => c.Code == code);
        if (currency is null) return code;
        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" ? currency.NameFa : currency.NameEn;
    }
}
