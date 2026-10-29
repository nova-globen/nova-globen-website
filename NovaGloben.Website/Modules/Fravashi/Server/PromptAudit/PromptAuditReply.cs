using System.Globalization;
using Fravashi.Broker.Containment;

namespace Fravashi.Broker.PromptAudit;

/// <summary>
/// What a person is told when inspection stops a request. Shaped as the provider's own error
/// (<c>{ type: "error", error: { type, message } }</c>) on purpose: the agent CLI shows a provider error's message
/// to the person in the conversation, so this is the one shape that reaches them rather than a log. The broker's
/// own figures ride beside it under <c>fravashi</c>, for a client that wants to show more. Nothing here repeats
/// the prompt, and a refusal names the categories and the figure but not the individual answers.
/// </summary>
public static class PromptAuditReply
{
    /// <summary>The refusal for an outcome that does not forward, or null when it forwards.</summary>
    public static ContainmentRefusal? For(PromptAuditOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Forwards)
        {
            return null;
        }

        return outcome.Decision switch
        {
            PromptAuditDecision.Refused => Refusal(
                StatusCodes.Status403Forbidden,
                "permission_error",
                "The organisation's broker did not forward this request: a message in this conversation was judged "
                + $"too risky to run (execution risk {Figure(outcome.Assessment?.Risk)}"
                + (outcome.Assessment is { Flagged.Count: > 0 } a ? $"; flagged: {string.Join(", ", a.Flagged)}" : string.Empty)
                + "). Every turn sends the whole conversation, so that message will be refused again: start a new "
                + "conversation, or remove it, to go on. If the judgement is wrong, an operator can find it on the "
                + "broker's audit line.",
                outcome),
            PromptAuditDecision.Unreadable => Refusal(
                StatusCodes.Status400BadRequest,
                "invalid_request_error",
                "The organisation's broker inspects every prompt before forwarding it, and could not read the prompts "
                + "out of this request, so it was not forwarded.",
                outcome),
            PromptAuditDecision.TooLarge => Refusal(
                StatusCodes.Status413RequestEntityTooLarge,
                "request_too_large",
                "The organisation's broker inspects every prompt before forwarding it, and this request holds more "
                + "new text than one request may have inspected, so it was not forwarded. Start a new conversation, "
                + "or send less at once.",
                outcome),
            _ => Refusal(
                StatusCodes.Status503ServiceUnavailable,
                "api_error",
                "The organisation's broker inspects every prompt before forwarding it, and the inspection service gave "
                + "no answer just now, so this request was not forwarded. Try again shortly.",
                outcome),
        };
    }

    private static ContainmentRefusal Refusal(int status, string type, string message, PromptAuditOutcome outcome) =>
        new(
            status,
            new
            {
                type = "error",
                error = new { type, message },
                fravashi = new
                {
                    reason = outcome.Reason,
                    risk = outcome.Assessment?.Risk,
                    flagged = outcome.Assessment?.Flagged ?? [],
                },
            },
            outcome.Reason);

    private static string Figure(double? risk) =>
        risk is { } r ? r.ToString("0.00", CultureInfo.InvariantCulture) : "unknown";
}
