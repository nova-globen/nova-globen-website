using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.PromptAudit;

/// <summary>
/// <see cref="ISystemOneModel"/> over TypeSafe's System One API (model Jev), the first adapter.
/// One call is <c>POST {Endpoint}</c> with <c>{ state, model, questions }</c> and a bearer key, and the answer is
/// <c>{ model, answers: { id: { type, ... } }, usage }</c> (https://docs.typesafe.ai/api). The key comes from
/// <see cref="BrokerSecrets.PromptAuditKey"/>, is added here and only here, and never appears in an exception,
/// a log line, or anything returned to a client.
/// <para>
/// A 429 or 529 earns ONE retry after <see cref="PromptAuditOptions.RetryDelay"/>, inside the same overall
/// <see cref="PromptAuditOptions.Timeout"/>, because TypeSafe asks for backoff on exactly those two. Everything
/// else that is not a readable answer to every question is a <see cref="SystemOneUnavailableException"/>.
/// </para>
/// </summary>
public sealed class TypeSafeSystemOne(
    HttpClient httpClient,
    BrokerSecrets secrets,
    IOptions<BrokerServiceOptions> options,
    TimeProvider clock) : ISystemOneModel
{
    private readonly PromptAuditOptions _options = options.Value.PromptAudit;

    public string Name => PromptAuditOptions.TypeSafe;

    public async Task<SystemOneResult> EvaluateAsync(
        JsonNode state, JsonObject questions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);

        var key = secrets.PromptAuditKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new SystemOneUnavailableException("no_key");
        }

        var payload = new JsonObject
        {
            ["state"] = state.DeepClone(),
            ["model"] = _options.Model,
            ["questions"] = questions.DeepClone(),
        }.ToJsonString();

        using var deadline = new CancellationTokenSource(_options.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.Accept.ParseAdd("application/json");

                using var response = await httpClient.SendAsync(request, linked.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if ((status == 429 || status == 529) && attempt == 1)
                {
                    await Task.Delay(_options.RetryDelay, clock, linked.Token).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new SystemOneUnavailableException("http_" + status);
                }

                var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
                return Parse(body, questions);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SystemOneUnavailableException("timeout");
        }
        catch (HttpRequestException ex)
        {
            throw new SystemOneUnavailableException("unreachable", ex);
        }
    }

    /// <summary>Reads an answer document. Internal so the answer shapes are tested without a network.</summary>
    internal static SystemOneResult Parse(string body, JsonObject questions)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException ex)
        {
            throw new SystemOneUnavailableException("unreadable_answer", ex);
        }

        if (root?["answers"] is not JsonObject answers)
        {
            throw new SystemOneUnavailableException("unreadable_answer");
        }

        var read = new Dictionary<string, SystemOneAnswer>(StringComparer.Ordinal);
        foreach (var (id, question) in questions)
        {
            if (answers[id] is not JsonObject answer)
            {
                throw new SystemOneUnavailableException("answer_missing_" + id);
            }

            var type = (string?)question?["type"];
            read[id] = type switch
            {
                "noul" => new NoulAnswer(Number(answer, "noul", id)),
                "choice" => new ChoiceAnswer(
                    answer["choice"] is JsonValue choice && choice.TryGetValue<string>(out var chosen)
                        ? chosen
                        : throw new SystemOneUnavailableException("answer_unreadable_" + id),
                    Probabilities(answer),
                    OptionalNumber(answer, "confidence")),
                "score" => new ScoreAnswer(
                    Number(answer, "score", id),
                    Levels(answer, question) is var levels and >= 2
                        ? levels
                        : throw new SystemOneUnavailableException("answer_unreadable_" + id),
                    OptionalNumber(answer, "confidence")),
                _ => throw new SystemOneUnavailableException("question_type_unknown_" + id),
            };
        }

        return new SystemOneResult(root["model"] is JsonValue m && m.TryGetValue<string>(out var model) ? model : null, read);
    }

    private static double Number(JsonObject answer, string field, string id) =>
        answer[field] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number)
            ? number
            : throw new SystemOneUnavailableException("answer_unreadable_" + id);

    private static double OptionalNumber(JsonObject answer, string field) =>
        answer[field] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : 0;

    private static Dictionary<string, double> Probabilities(JsonObject answer)
    {
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        if (answer["probabilities"] is JsonObject map)
        {
            foreach (var (option, value) in map)
            {
                if (value is JsonValue v && v.TryGetValue<double>(out var p) && double.IsFinite(p))
                {
                    probabilities[option] = p;
                }
            }
        }

        return probabilities;
    }

    /// <summary>How many levels the rubric has: the answer's own legend when it sends one, else the question's.</summary>
    private static int Levels(JsonObject answer, JsonNode? question)
    {
        if (answer["legend"] is JsonObject legend && legend.Count > 0)
        {
            return legend.Count;
        }

        return question?["criteria"] is JsonArray criteria ? criteria.Count : 0;
    }
}
