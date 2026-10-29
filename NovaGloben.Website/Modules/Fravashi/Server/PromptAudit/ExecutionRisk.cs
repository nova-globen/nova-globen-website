namespace Fravashi.Broker.PromptAudit;

/// <summary>Where an execution risk falls against the configured thresholds.</summary>
public enum RiskBand
{
    /// <summary>Below the caution threshold: forwarded.</summary>
    Allowed,

    /// <summary>At or above the caution threshold and below the refusal one: forwarded, and recorded as such.</summary>
    Cautioned,

    /// <summary>At or above the refusal threshold: not forwarded.</summary>
    Refused,
}

/// <summary>What one judgement comes to.</summary>
/// <param name="Risk">The execution risk, 0..1.</param>
/// <param name="Band">Where it falls.</param>
/// <param name="Flagged">The categories whose yes-value reached the flag threshold, highest first.</param>
/// <param name="Confidence">The lower of the intent and danger confidences, for the audit line.</param>
public sealed record RiskAssessment(double Risk, RiskBand Band, IReadOnlyList<string> Flagged, double Confidence)
{
    /// <summary>The riskier of two assessments, with both sets of flags. How pieces of one request add up.</summary>
    public static RiskAssessment Worse(RiskAssessment a, RiskAssessment b)
    {
        var worse = b.Risk > a.Risk ? b : a;
        return worse with
        {
            Flagged = [.. a.Flagged.Concat(b.Flagged).Distinct(StringComparer.Ordinal)],
            Confidence = Math.Min(a.Confidence, b.Confidence),
        };
    }
}

/// <summary>
/// Turns a System One model's answers into ONE execution risk. The model answers narrow questions
/// and the composition is ours, in code, where it can be read and tested (TypeSafe's own guidance: keep the
/// judgements atomic and compose them deterministically).
/// <para>The rule, in full:</para>
/// <list type="bullet">
/// <item><b>Intent risk</b> = P(harmful) + <c>SensitiveIntentWeight</c> x P(sensitive_but_legitimate).</item>
/// <item><b>Danger risk</b> = the overall-danger score placed on 0..1 (Safe 0, Caution 0.5, Dangerous 1).</item>
/// <item><b>Category risk</b> = the highest (yes-value x that category's weight) over every yes/no question.</item>
/// <item><b>Execution risk</b> = the greater of the category risk and the mean of intent and danger.</item>
/// </list>
/// <para>
/// Why that shape: a single category the model is sure of has to be able to stand alone (a prompt that is
/// clearly about harming a child must not be averaged down by eleven other categories saying no), and the two
/// broad questions corroborate each other rather than either one refusing by itself. Probabilities are used rather
/// than the chosen option, because they are calibrated and a 55/45 call should not weigh what a 99/1 one does.
/// </para>
/// <para>
/// An answer that is missing, or has the wrong shape, is not guessed around: it is a <see
/// cref="SystemOneUnavailableException"/>, so a service that answered half the questions is treated as a
/// service that did not answer.
/// </para>
/// </summary>
public static class ExecutionRisk
{
    public static RiskAssessment Assess(SystemOneResult result, PromptAuditOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);

        if (!result.Answers.TryGetValue(PromptAuditQuestions.PrimaryIntent, out var intentAnswer)
            || intentAnswer is not ChoiceAnswer intent)
        {
            throw new SystemOneUnavailableException("answer_missing_" + PromptAuditQuestions.PrimaryIntent);
        }

        if (!result.Answers.TryGetValue(PromptAuditQuestions.OverallDanger, out var dangerAnswer)
            || dangerAnswer is not ScoreAnswer danger)
        {
            throw new SystemOneUnavailableException("answer_missing_" + PromptAuditQuestions.OverallDanger);
        }

        var intentRisk = Math.Clamp(
            Probability(intent, PromptAuditQuestions.Harmful)
            + (options.SensitiveIntentWeight * Probability(intent, PromptAuditQuestions.SensitiveButLegitimate)),
            0,
            1);
        var blended = (intentRisk + danger.Normalized) / 2;

        var categoryRisk = 0.0;
        var flagged = new List<(string Id, double Value)>();
        foreach (var category in PromptAuditQuestions.Categories())
        {
            if (!result.Answers.TryGetValue(category, out var answer) || answer is not NoulAnswer noul)
            {
                throw new SystemOneUnavailableException("answer_missing_" + category);
            }

            var value = Math.Clamp(noul.Value, 0, 1);
            var weight = options.CategoryWeights.TryGetValue(category, out var w) ? w : options.DefaultCategoryWeight;
            categoryRisk = Math.Max(categoryRisk, value * Math.Clamp(weight, 0, 1));
            if (value >= options.FlagAt)
            {
                flagged.Add((category, value));
            }
        }

        var risk = Math.Round(Math.Max(categoryRisk, blended), 4);
        var band = risk >= options.RefuseAt ? RiskBand.Refused
            : risk >= options.CautionAt ? RiskBand.Cautioned
            : RiskBand.Allowed;

        return new RiskAssessment(
            risk,
            band,
            [.. flagged.OrderByDescending(f => f.Value).Select(f => f.Id)],
            Math.Min(intent.Confidence, danger.Confidence));
    }

    /// <summary>
    /// An option's probability. A service that names its choice but gives no probabilities is read as certain of
    /// that choice, which errs toward the choice rather than toward nothing.
    /// </summary>
    private static double Probability(ChoiceAnswer answer, string option)
    {
        if (answer.Probabilities.Count == 0)
        {
            return string.Equals(answer.Choice, option, StringComparison.Ordinal) ? 1 : 0;
        }

        return answer.Probabilities.TryGetValue(option, out var p) ? Math.Clamp(p, 0, 1) : 0;
    }
}
