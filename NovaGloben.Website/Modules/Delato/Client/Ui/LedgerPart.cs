using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Delato.Client.Ui;

/// <summary>
/// The base of every part of the ledger page: the shared context, the texts, and ids for its form fields. Every input
/// carries an id its label names with <c>for</c>, unique to the part's instance, so assistive technology names each
/// field.
/// </summary>
public abstract class LedgerPart : ComponentBase
{
    private readonly string _uid = Guid.NewGuid().ToString("N")[..8];

    [Parameter, EditorRequired] public LedgerContext Context { get; set; } = default!;

    protected IStringLocalizer<DelatoText> T => Context.T;

    /// <summary>The id of one of this part's fields: <c>delato-1a2b3c4d-amount</c>.</summary>
    protected string Id(string field) => $"delato-{_uid}-{field}";
}
