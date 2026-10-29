using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Fravashi.Broker.PromptAudit;

/// <summary>
/// The questions every user prompt is asked, exactly as the product sets them, kept in
/// <c>prompt-audit-questions.json</c> beside this file and embedded in the assembly. They are data, not code,
/// because they ARE the policy and should read as one, and because the System One shape lets them change without
/// the weighting knowing: the weighting reads <see cref="PrimaryIntent"/>, <see cref="OverallDanger"/>, and then
/// every yes/no question it finds, whatever they are called.
/// <para>
/// The prompt goes in as <c>state.prompt</c>, and every question names it as <c>`prompt`</c>, which is how a
/// System One model is pointed at one value in its state.
/// </para>
/// </summary>
public static class PromptAuditQuestions
{
    /// <summary>The choice question the intent risk is read from: benign, sensitive_but_legitimate, harmful.</summary>
    public const string PrimaryIntent = "primary_intent";

    /// <summary>The score question the danger risk is read from: Safe, Caution, Dangerous.</summary>
    public const string OverallDanger = "overall_danger";

    /// <summary>The option of <see cref="PrimaryIntent"/> that counts in full.</summary>
    public const string Harmful = "harmful";

    /// <summary>The option of <see cref="PrimaryIntent"/> that counts at the sensitive-intent weight.</summary>
    public const string SensitiveButLegitimate = "sensitive_but_legitimate";

    private const string ResourceName = "Fravashi.Broker.PromptAudit.prompt-audit-questions.json";

    private static readonly Lazy<(string Json, string Version)> Source = new(() =>
    {
        using var stream = typeof(PromptAuditQuestions).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded question set {ResourceName} is missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
        return (json, version);
    });

    /// <summary>
    /// A fingerprint of the question set. Remembered judgements are keyed by it as well as by the text, so a
    /// changed question set judges everything again rather than trusting answers to questions nobody asks now.
    /// </summary>
    public static string Version => Source.Value.Version;

    /// <summary>A fresh copy of the question set, safe for the caller to hand to a serializer.</summary>
    public static JsonObject Load() =>
        JsonNode.Parse(Source.Value.Json)?.AsObject()
        ?? throw new InvalidOperationException("The embedded question set is not a JSON object.");

    /// <summary>The ids of the yes/no (category) questions, in file order.</summary>
    public static IReadOnlyList<string> Categories() =>
        [.. Load().Where(q => (string?)q.Value?["type"] == "noul").Select(q => q.Key)];
}
