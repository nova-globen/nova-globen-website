using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.PromptAudit;

/// <summary>How one request's inspection came out.</summary>
public enum PromptAuditDecision
{
    /// <summary>Inspection is off, or this path generates nothing (counting tokens).</summary>
    NotInspected,

    /// <summary>Judged and below the caution threshold.</summary>
    Allowed,

    /// <summary>Judged, forwarded, and recorded as cautioned.</summary>
    Cautioned,

    /// <summary>Judged too risky to forward.</summary>
    Refused,

    /// <summary>The body is not one this broker can read the prompts out of.</summary>
    Unreadable,

    /// <summary>The request needs more calls than one request may make.</summary>
    TooLarge,

    /// <summary>The model gave no judgement. Forwarded or not by <see cref="PromptAuditOptions.WhenUnavailable"/>.</summary>
    Unavailable,
}

/// <summary>The outcome of inspecting one request. It carries figures and category names only, never the prompt.</summary>
/// <param name="Decision">What was decided.</param>
/// <param name="Forwards">Whether the request goes upstream.</param>
/// <param name="Assessment">The request's risk when it was judged; null otherwise.</param>
/// <param name="Reason">A short, secret-free reason for the audit line.</param>
/// <param name="Calls">How many calls the model was asked for this request (zero when every text was remembered).</param>
public sealed record PromptAuditOutcome(
    PromptAuditDecision Decision,
    bool Forwards,
    RiskAssessment? Assessment,
    string Reason,
    int Calls = 0)
{
    public static readonly PromptAuditOutcome NotInspected =
        new(PromptAuditDecision.NotInspected, true, null, "not_inspected");
}

/// <summary>
/// Inspects the user prompts of every request the broker forwards (the product's rule: every
/// user prompt is inspected by a System One model, TypeSafe's first). It reads the prompts out
/// of the body (<see cref="UserPrompts"/>), asks the configured <see cref="ISystemOneModel"/> the product's
/// questions about each (<see cref="PromptAuditQuestions"/>), weighs the answers into one execution risk
/// (<see cref="ExecutionRisk"/>), and decides.
/// <para>
/// Three things keep it cheap and whole at once. A text's judgement is REMEMBERED by the SHA-256 of the
/// question-set version and the text, never by the text, so the conversation that rides along in every request
/// is judged once and not every turn. Short new texts are PACKED into one call up to
/// <see cref="PromptAuditOptions.MaxChunkChars"/>, and a pack that comes back anything but allowed is asked
/// again text by text, so one risky message is not remembered against the innocent ones it was packed with. And
/// a text longer than that is CUT into overlapping pieces, each judged, the text as risky as its riskiest piece,
/// so padding cannot dilute it.
/// </para>
/// <para>
/// A refused message stays refused for the rest of its conversation, because the conversation keeps sending it.
/// That is deliberate and the refusal says so: judging only the newest message would forward the refused one on
/// the very next turn.
/// </para>
/// </summary>
public sealed class PromptAuditor
{
    /// <summary>How much consecutive pieces of one long text overlap, so a phrase cut in two is whole in one of them.</summary>
    internal const int PieceOverlap = 500;

    private const string PackSeparator = "\n\n";

    private readonly ISystemOneModel? _model;
    private readonly PromptAuditOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (RiskAssessment Assessment, DateTimeOffset Expires)> _remembered =
        new(StringComparer.Ordinal);

    public PromptAuditor(ISystemOneModel? model, IOptions<BrokerServiceOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _model = model;
        _options = options.Value.PromptAudit;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>True when a model is configured, so every generating request is inspected.</summary>
    public bool IsOn => _model is not null;

    /// <summary>The configured model's name, for the start-up log; null when off.</summary>
    public string? ModelName => _model?.Name;

    /// <summary>How many judgements are remembered right now. For tests and the operator's figures.</summary>
    public int Remembered => _remembered.Count;

    /// <summary>
    /// Whether a path generates anything and is therefore inspected. Counting tokens generates nothing, and every
    /// other POST the broker forwards might, so the rule is "everything but that" rather than a list of the paths
    /// that generate: a path nobody listed is inspected, not waved through.
    /// </summary>
    public static bool Inspects(string path) =>
        !(path ?? string.Empty).TrimEnd('/').EndsWith("/count_tokens", StringComparison.OrdinalIgnoreCase);

    public async Task<PromptAuditOutcome> AuditAsync(string path, byte[] body, CancellationToken cancellationToken)
    {
        if (_model is null || !Inspects(path))
        {
            return PromptAuditOutcome.NotInspected;
        }

        var read = UserPrompts.Read(body);
        if (!read.Readable)
        {
            return new PromptAuditOutcome(PromptAuditDecision.Unreadable, false, null, "prompt_unreadable");
        }

        var now = _clock.GetUtcNow();
        var texts = read.Texts.Distinct(StringComparer.Ordinal).ToList();
        var known = new List<RiskAssessment>();
        var fresh = new List<(string Text, string Key)>();
        foreach (var text in texts)
        {
            var key = KeyOf(text);
            if (_remembered.TryGetValue(key, out var held) && held.Expires > now)
            {
                known.Add(held.Assessment);
            }
            else
            {
                fresh.Add((text, key));
            }
        }

        var calls = 0;
        if (fresh.Count > 0)
        {
            Dictionary<string, RiskAssessment> judged;
            try
            {
                (judged, calls) = await JudgeAsync(fresh, cancellationToken).ConfigureAwait(false);
            }
            catch (TooLargeException tooLarge)
            {
                return new PromptAuditOutcome(PromptAuditDecision.TooLarge, false, null, "prompt_too_large", tooLarge.Calls);
            }
            catch (SystemOneUnavailableException unavailable)
            {
                var forwards = _options.WhenUnavailable == PromptAuditFallback.Forward;
                return new PromptAuditOutcome(
                    PromptAuditDecision.Unavailable, forwards, null, "prompt_audit_unavailable_" + unavailable.Reason);
            }

            var expires = now + _options.VerdictTtl;
            foreach (var (key, assessment) in judged)
            {
                Remember(key, assessment, expires, now);
                known.Add(assessment);
            }
        }

        if (known.Count == 0)
        {
            // A request with no user text at all (every user message is tool output): nothing a person wrote is in
            // it, and what they wrote earlier in the conversation was judged when it arrived.
            return new PromptAuditOutcome(PromptAuditDecision.Allowed, true, null, "prompt_none", calls);
        }

        var overall = known.Aggregate(RiskAssessment.Worse);
        return overall.Band switch
        {
            RiskBand.Refused => new PromptAuditOutcome(PromptAuditDecision.Refused, false, overall, "prompt_refused", calls),
            RiskBand.Cautioned => new PromptAuditOutcome(PromptAuditDecision.Cautioned, true, overall, "prompt_cautioned", calls),
            _ => new PromptAuditOutcome(PromptAuditDecision.Allowed, true, overall, "prompt_allowed", calls),
        };
    }

    /// <summary>
    /// Judges the new texts: packs the short ones, cuts the long ones, then asks again one by one for any pack
    /// that did not come back allowed. Returns each text's judgement by its key, and how many calls it took.
    /// </summary>
    private async Task<(Dictionary<string, RiskAssessment> Judged, int Calls)> JudgeAsync(
        List<(string Text, string Key)> fresh, CancellationToken cancellationToken)
    {
        var max = _options.MaxChunkChars;
        var packs = new List<List<(string Text, string Key)>>();
        var pieces = new List<(string Key, string Piece)>();
        var current = new List<(string Text, string Key)>();
        var currentLength = 0;

        foreach (var item in fresh)
        {
            if (item.Text.Length > max)
            {
                pieces.AddRange(Cut(item.Text, max).Select(piece => (item.Key, piece)));
                continue;
            }

            if (current.Count > 0 && currentLength + PackSeparator.Length + item.Text.Length > max)
            {
                packs.Add(current);
                current = [];
                currentLength = 0;
            }

            currentLength += (current.Count > 0 ? PackSeparator.Length : 0) + item.Text.Length;
            current.Add(item);
        }

        if (current.Count > 0)
        {
            packs.Add(current);
        }

        var calls = packs.Count + pieces.Count;
        if (calls > _options.MaxCallsPerRequest)
        {
            throw new TooLargeException(calls);
        }

        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxParallelCalls));
        var packResults = await Task.WhenAll(packs.Select(pack =>
            JudgeOneAsync(string.Join(PackSeparator, pack.Select(p => p.Text)), gate, cancellationToken)))
            .ConfigureAwait(false);
        var pieceResults = await Task.WhenAll(pieces.Select(piece =>
            JudgeOneAsync(piece.Piece, gate, cancellationToken))).ConfigureAwait(false);

        var judged = new Dictionary<string, RiskAssessment>(StringComparer.Ordinal);
        var recheck = new List<(string Text, string Key)>();
        for (var i = 0; i < packs.Count; i++)
        {
            if (packs[i].Count > 1 && packResults[i].Band != RiskBand.Allowed)
            {
                recheck.AddRange(packs[i]);
            }
            else
            {
                foreach (var (_, key) in packs[i])
                {
                    judged[key] = packResults[i];
                }
            }
        }

        for (var i = 0; i < pieces.Count; i++)
        {
            var key = pieces[i].Key;
            judged[key] = judged.TryGetValue(key, out var sofar) ? RiskAssessment.Worse(sofar, pieceResults[i]) : pieceResults[i];
        }

        if (recheck.Count > 0)
        {
            calls += recheck.Count;
            if (calls > _options.MaxCallsPerRequest)
            {
                throw new TooLargeException(calls);
            }

            var alone = await Task.WhenAll(recheck.Select(item => JudgeOneAsync(item.Text, gate, cancellationToken)))
                .ConfigureAwait(false);
            for (var i = 0; i < recheck.Count; i++)
            {
                judged[recheck[i].Key] = alone[i];
            }
        }

        return (judged, calls);
    }

    private async Task<RiskAssessment> JudgeOneAsync(string prompt, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _model!.EvaluateAsync(
                new JsonObject { ["prompt"] = prompt }, PromptAuditQuestions.Load(), cancellationToken).ConfigureAwait(false);
            return ExecutionRisk.Assess(result, _options);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Overlapping pieces of at most <paramref name="max"/> characters that together cover the text.</summary>
    internal static IEnumerable<string> Cut(string text, int max)
    {
        var step = Math.Max(1, max - Math.Min(PieceOverlap, max / 2));
        for (var start = 0; ; start += step)
        {
            var length = Math.Min(max, text.Length - start);
            yield return text.Substring(start, length);
            if (start + length >= text.Length)
            {
                yield break;
            }
        }
    }

    private static string KeyOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(PromptAuditQuestions.Version + "\n" + text)));

    private void Remember(string key, RiskAssessment assessment, DateTimeOffset expires, DateTimeOffset now)
    {
        if (_remembered.Count >= _options.VerdictCacheEntries)
        {
            foreach (var stale in _remembered.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToList())
            {
                _remembered.TryRemove(stale, out _);
            }

            if (_remembered.Count >= _options.VerdictCacheEntries)
            {
                // Forgetting costs a second judgement and nothing else, so the bound is kept simply.
                _remembered.Clear();
            }
        }

        _remembered[key] = (assessment, expires);
    }

    private sealed class TooLargeException(int calls) : Exception
    {
        public int Calls { get; } = calls;
    }
}
