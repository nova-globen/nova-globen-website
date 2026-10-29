namespace Fravashi.Broker.Containment;

/// <summary>A request the broker will not send upstream: the status, what the caller is told, and the audit reason.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The JSON body. Secret-free, and specific enough for the caller to know what to do next.</param>
/// <param name="Reason">The short reason the audit line carries.</param>
public sealed record ContainmentRefusal(int Status, object Body, string Reason);

/// <summary>
/// What is decided around one forwarded request: whether it may go upstream at all, and what it is
/// charged once the answer has passed. Kept out of the endpoint so it can be tested without a server; the
/// endpoint does the plumbing and asks this for every decision.
/// </summary>
public sealed class ForwardContainment(ModelPolicy models, ITokenBudget budget, Suspensions suspensions, TokenWeights? weights = null)
{
    /// <summary>The one path whose answers GENERATE tokens and are therefore charged.</summary>
    public const string MessagesPath = "/v1/messages";

    /// <summary>
    /// A user this broker has stopped serving, or null. Asked before anything else on both the mint and the
    /// forward path: a suspension that did not stop the tokens a user already holds would stop nothing for an
    /// hour, and one that let them mint a fresh token would stop nothing at all.
    /// </summary>
    public ContainmentRefusal? Suspended(string userId)
    {
        if (suspensions.Of(userId) is not { } held)
        {
            return null;
        }

        return new ContainmentRefusal(
            StatusCodes.Status403Forbidden,
            new
            {
                error = "suspended",
                since = held.Since,
                message = "This broker has stopped serving you after repeated refusals. An operator can lift it.",
            },
            "suspended");
    }

    /// <summary>
    /// Counts a refusal that happened outside <see cref="Before"/> (the rate limit and the request quota), so
    /// every way a user can be refused adds up in ONE place. A suspension that only counted some refusals
    /// would be a wall a loop could walk around by changing which wall it hits.
    /// </summary>
    public void Refused(string userId, string reason) => suspensions.Refused(userId, reason);

    /// <summary>
    /// Decided BEFORE the org key is reached. The suspension first, then the model, because a request the grant
    /// does not cover should be refused for that reason even when the budget is also spent; then the budgets.
    /// Every refusal this makes is counted toward a suspension.
    /// </summary>
    public ContainmentRefusal? Before(string userId, string? model)
    {
        if (Suspended(userId) is { } stopped)
        {
            return stopped;
        }

        var covered = models.Check(model);
        if (!covered.Covered)
        {
            var refusal = new ContainmentRefusal(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = covered.Reason,
                    model = covered.Model,
                    message = covered.Model is null
                        ? "This broker only forwards requests for the models its grant covers, and no model could be read from this request."
                        : $"This broker's grant does not cover the model '{covered.Model}'.",
                },
                covered.Reason!);
            suspensions.Refused(userId, covered.Reason!);
            return refusal;
        }

        var room = budget.Check(userId);
        if (!room.Allowed)
        {
            var whose = room.Limit == "organisation" ? "The organisation's" : "Your";
            suspensions.Refused(userId, "token_budget_spent_" + room.Limit);
            return new ContainmentRefusal(
                StatusCodes.Status429TooManyRequests,
                new
                {
                    error = "token_budget_spent",
                    limit = room.Limit,
                    resets_at = room.ResetsAt,
                    message = $"{whose} token budget for this period is spent. It resets at {room.ResetsAt:O}.",
                },
                "token_budget_spent_" + room.Limit);
        }

        return null;
    }

    /// <summary>
    /// Charged AFTER the answer has passed. Only a successful answer to the messages path is charged: a refusal
    /// from the provider generated nothing, and the other paths (counting tokens, listing models) generate
    /// nothing either, although their answers contain the same field names. The budget is charged the answer's
    /// weighted units (<see cref="TokenWeights"/>); returns them, or null when the usage could not be read (the budget
    /// then charges its worst case).
    /// </summary>
    public long? After(string userId, string path, int status, TokenUsage? usage)
    {
        var generated = status is >= 200 and < 300
            && string.Equals(path.TrimEnd('/'), MessagesPath, StringComparison.OrdinalIgnoreCase);
        if (!generated)
        {
            return 0;
        }

        var units = usage is null ? (long?)null : (weights ?? new TokenWeights()).Units(usage);
        budget.Charge(userId, units, usage?.Total);
        return units;
    }
}
