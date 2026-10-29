using System.Text.Json.Nodes;

namespace Fravashi.Broker.PromptAudit;

/// <summary>
/// A "System One" model: something that answers TYPED questions about a state in one fast call,
/// rather than writing prose. The broker asks one about every user prompt it forwards. This is the port and the
/// only thing the rest of the broker knows about the service, so replacing the provider with another System One
/// model is a new adapter and a
/// configuration line, never a change to the questions, the weighting, or the forward path.
/// <para>
/// An adapter's contract: send <paramref name="state"/> and <paramref name="questions"/> as given, and answer
/// every question with one of the three answer shapes. Anything else (an unreachable service, a refusal, an
/// answer it cannot read, a question left unanswered) is a <see cref="SystemOneUnavailableException"/>, never a
/// guess, because the caller's whole job is deciding what to do when there IS no judgement.
/// </para>
/// </summary>
public interface ISystemOneModel
{
    /// <summary>A short, secret-free name for the audit line and the start-up log, e.g. <c>typesafe</c>.</summary>
    string Name { get; }

    /// <summary>Asks every question in <paramref name="questions"/> about <paramref name="state"/>.</summary>
    Task<SystemOneResult> EvaluateAsync(JsonNode state, JsonObject questions, CancellationToken cancellationToken);
}

/// <summary>What a System One model answered.</summary>
/// <param name="ModelVersion">The model version the service says did the work, when it says; for the audit.</param>
/// <param name="Answers">One answer per question, keyed by the question's id.</param>
public sealed record SystemOneResult(string? ModelVersion, IReadOnlyDictionary<string, SystemOneAnswer> Answers);

/// <summary>One typed answer. The three shapes are the three question types every System One model offers.</summary>
public abstract record SystemOneAnswer;

/// <summary>A yes/no judgement as a value from 0 (no) to 1 (yes).</summary>
public sealed record NoulAnswer(double Value) : SystemOneAnswer;

/// <summary>Which option was chosen, with every option's probability and the model's confidence.</summary>
public sealed record ChoiceAnswer(string Choice, IReadOnlyDictionary<string, double> Probabilities, double Confidence)
    : SystemOneAnswer;

/// <summary>
/// A position on a rubric of <paramref name="Levels"/> ordered levels, as the expected level index (0 is the
/// first level, <c>Levels - 1</c> the last; fractional between them), with the model's confidence.
/// </summary>
public sealed record ScoreAnswer(double Score, int Levels, double Confidence) : SystemOneAnswer
{
    /// <summary>The score placed on 0..1, so a rubric of any length weighs the same.</summary>
    public double Normalized => Levels <= 1 ? 0 : Math.Clamp(Score / (Levels - 1), 0, 1);
}

/// <summary>
/// There is no judgement to be had: the service could not be reached, refused, answered late, or answered in a
/// way that cannot be read. The <see cref="Reason"/> is short and secret-free (it goes on the audit line), and
/// no message ever carries the service's key or the prompt.
/// </summary>
public sealed class SystemOneUnavailableException(string reason, Exception? inner = null)
    : Exception($"The System One model gave no usable judgement ({reason}).", inner)
{
    public string Reason { get; } = reason;
}
