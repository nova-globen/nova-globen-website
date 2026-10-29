using Delato.Shared.Contracts;

namespace Delato.Client.Services;

/// <summary>One kind of record in an import's answer: how many the file carried, and how many were created or skipped.</summary>
public sealed record ImportLine(string Kind, int Carried, int Created, int Skipped);

/// <summary>An import's answer, per kind, for showing it plainly.</summary>
public static class ImportSummary
{
    /// <summary>Groups, expenses and settlements: carried, created, skipped (skipped records are named "group 3" etc.).</summary>
    public static List<ImportLine> Lines(ImportResult result)
    {
        int Skipped(string kind) => result.Skipped.Count(s => s.StartsWith(kind + " ", StringComparison.Ordinal));
        var groups = Skipped("group");
        var expenses = Skipped("expense");
        var settlements = Skipped("settlement");
        return
        [
            new("Groups", result.Groups, result.Groups - groups, groups),
            new("Expenses", result.Expenses, result.Expenses - expenses, expenses),
            new("Settlements", result.Settlements, result.Settlements - settlements, settlements),
        ];
    }

    /// <summary>The largest file the page reads: 1 MB.</summary>
    public const long MaxBytes = 1024 * 1024;
}
