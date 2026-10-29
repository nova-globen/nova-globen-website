using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Stomme.Modules.AiAssistant.Application;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// "Ask Didin": ten titles for what the user is in the mood for, chosen with their own ratings, what they have seen,
    /// their list and what they are part-way through. The model is Stomme's assistant model for the site
    /// (IAssistantModelAccess: the provider, endpoint, model and key the site set in its assistant settings, with every
    /// call quota-checked and metered), behind Stomme's guardrail (rate window, injection phrases). Dormant until the site
    /// switches its assistant on: until then the status says so and nothing calls a model. Every signed-in user may ask.
    /// The model only ever sees catalogue ids it may pick from; an id it invents is dropped.
    /// </summary>
    [ApiController]
    [Route("api/advisor")]
    [Authorize]
    public sealed class AdvisorController(IAssistantModelAccess models, IAssistantGuardrail guardrail, IAssistantCaller caller,
        DidinStore store, CatalogQuery query, ILogger<AdvisorController> log) : DidinController
    {
        private const int PickCount = 10;
        private const int MaxMessage = 2000;
        private const int MaxTurns = 20;

        // GET {site}/api/advisor/status  ->  is the assistant switched on and configured for this site?
        [HttpGet("status")]
        public async Task<AdvisorStatus> Status()
        {
            try
            {
                return (await models.IsAvailableAsync(HttpContext.RequestAborted))
                    ? new AdvisorStatus { Available = true }
                    : new AdvisorStatus { Reason = "The assistant is not switched on for this site yet." };
            }
            catch (Exception ex)
            {
                return new AdvisorStatus { Reason = AssistantRefusals.Describe(ex)?.View.Message ?? "The assistant is not available." };
            }
        }

        // POST {site}/api/advisor/suggest  { message, history, exclude }  ->  { reply, picks[10] } or { error }. Bounded:
        // the whole answer within Budget, so it is back before an HTTP client gives up (100 s by default)
        [HttpPost("suggest")]
        public async Task<ActionResult<AdvisorReply>> Suggest([FromBody] AdvisorRequest request)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Forbid();
            if (request == null || (request.Message?.Length ?? 0) > MaxMessage || request.History.Count > MaxTurns ||
                request.History.Any(t => (t.Text?.Length ?? 0) > 4000))
            {
                return BadRequest();
            }
            return await SuggestAsync(request, userId);
        }

        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(80);

        private async Task<AdvisorReply> SuggestAsync(AdvisorRequest request, int userId)
        {
            using var budget = new System.Threading.CancellationTokenSource(Budget);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var (siteId, _, callerId) = await caller.ResolveAsync();
                // the guardrail reads what the USER wrote: this message and the earlier ones
                var userText = request.History.Where(t => t.Role == "user").Select(t => t.Text)
                    .Append(request.Message).Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => new AssistantMessage(ChatRole.User, t)).ToList();
                if (userText.Count > 0)
                {
                    await guardrail.EnsureAllowedAsync(new AssistantRequest(Guid.Empty, userText), siteId, callerId);
                }

                var catalog = await query.GetCatalogAsync(Site, HttpContext.RequestAborted);
                var profile = await ProfileAsync(userId, catalog);
                var exclude = request.Exclude.ToHashSet();
                var candidates = catalog.Cards
                    .Where(c => !profile.Seen.Contains(c.Id) && !profile.Rated.ContainsKey(c.Id) && !exclude.Contains(c.Id))
                    .ToList();
                if (candidates.Count == 0)
                {
                    return new AdvisorReply { Reply = "You have seen or turned down everything in the catalog - there is nothing left to suggest." };
                }

                var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt(profile, catalog, candidates)) };
                foreach (var turn in request.History.TakeLast(MaxTurns))
                {
                    messages.Add(new ChatMessage(turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, turn.Text ?? ""));
                }
                messages.Add(new ChatMessage(ChatRole.User, string.IsNullOrWhiteSpace(request.Message)
                    ? "None of those suit me. Suggest ten different titles."
                    : request.Message.Trim()));

                using var model = await models.OpenAsync("movie-advisor");
                var options = new ChatOptions
                {
                    Temperature = model.Temperature,
                    MaxOutputTokens = 4000,
                    // some models think before they answer unless told not to ("thinking": {"type": "disabled"}); an
                    // OpenAI-compatible server that does not know the field ignores it
                    RawRepresentationFactory = _ =>
                    {
                        var raw = new OpenAI.Chat.ChatCompletionOptions();
                        // Patch (JsonPatch) is how the SDK sends a field it does not model; it is marked "evaluation"
                        // (SCME0001) in OpenAI 2.14 - if it changes, the advisor still works, only with the model's thinking on
#pragma warning disable SCME0001
                        raw.Patch.Set("$.thinking"u8, BinaryData.FromString("{\"type\":\"disabled\"}"));
#pragma warning restore SCME0001
                        return raw;
                    },
                };
                var byId = candidates.ToDictionary(c => c.Id);
                var picks = new List<AdvisorPick>();
                string reply = null;
                // a second turn only fills gaps, and only while half the budget is left
                for (var attempt = 0; attempt < 2 && picks.Count < PickCount && (attempt == 0 || elapsed.Elapsed < Budget / 2); attempt++)
                {
                    var response = await model.Client.GetResponseAsync(messages, options, budget.Token);
                    var (said, offered) = Parse(response.Text);
                    reply ??= said;
                    foreach (var pick in offered)
                    {
                        if (picks.Count >= PickCount) break;
                        if (byId.ContainsKey(pick.TitleId) && picks.All(p => p.TitleId != pick.TitleId)) picks.Add(pick);
                    }
                    if (picks.Count < PickCount)
                    {
                        // one more turn: name what was kept, ask for the rest from the list only
                        messages.Add(new ChatMessage(ChatRole.Assistant, response.Text ?? ""));
                        messages.Add(new ChatMessage(ChatRole.User,
                            $"Only {picks.Count} of those are ids from the list ({string.Join(", ", picks.Select(p => p.TitleId))}). " +
                            $"Answer again in the same JSON with {PickCount - picks.Count} more, different ids taken ONLY from the candidate list."));
                    }
                }
                return new AdvisorReply { Reply = reply ?? "Here are some ideas.", Picks = picks };
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                return new AdvisorReply { Error = "The assistant took too long to answer. Try again, or ask for something narrower." };
            }
            catch (Exception ex) when (AssistantRefusals.Describe(ex) is { } refusal)
            {
                return new AdvisorReply { Error = refusal.View.Message };
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Ask Didin failed");
                return new AdvisorReply { Error = "The assistant could not answer just now. Try again in a moment." };
            }
        }

        private sealed class Profile
        {
            public Dictionary<int, (int Stars, string Review)> Rated { get; } = new();
            public HashSet<int> Seen { get; } = new();
            public List<int> Watchlist { get; } = new();
            public List<int> Watching { get; } = new();
        }

        private async Task<Profile> ProfileAsync(int userId, CatalogDocument catalog)
        {
            var profile = new Profile();
            var ct = HttpContext.RequestAborted;
            foreach (var r in (await store.AllAsync<RatingDoc>(Site.SiteId, DidinKind.Rating, ct)).Where(r => r.UserId == userId))
            {
                profile.Rated[r.TitleId] = (r.Stars, r.Review);
            }
            var state = await store.LoadAsync<UserStateDoc>(Site.SiteId, DidinKind.UserState, userId, ct: ct);
            if (state != null)
            {
                foreach (var m in state.Seen) profile.Seen.Add(m.TitleId);
                profile.Watchlist.AddRange(state.Watchlist);
                profile.Watching.AddRange(state.Progress.Values
                    .Where(p => p.TitleId != null && p.Position >= 30)
                    .OrderByDescending(p => p.UpdatedUtc).Select(p => p.TitleId.Value).Distinct().Take(20));
            }
            return profile;
        }

        private static string SystemPrompt(Profile profile, CatalogDocument catalog, List<CatalogCard> candidates)
        {
            var cards = catalog.Cards.ToDictionary(c => c.Id);
            string Name(int id) => cards.TryGetValue(id, out var c) ? $"{c.Title}{(c.Year != null ? $" ({c.Year})" : "")}" : null;

            var sb = new StringBuilder();
            sb.AppendLine("You are Didin, the film and series advisor of Didin, for one viewer. Suggest titles ONLY from the CANDIDATES list below - the viewer's own library.");
            sb.AppendLine("Use what the viewer liked and disliked (their ratings, 1-5 stars, and reviews), what they have seen, their list and what they are watching, together with what they ask for now.");
            sb.AppendLine("Never suggest a title that is not in CANDIDATES. Prefer variety, but follow the request (a genre, a mood, a length, movie or series).");
            sb.AppendLine();
            sb.AppendLine("Answer with ONE JSON object and nothing else:");
            sb.AppendLine("{\"reply\": \"one or two friendly sentences to the viewer\", \"picks\": [{\"id\": <candidate id>, \"reason\": \"why it suits them, at most 20 words\"}]}");
            sb.AppendLine($"Give exactly {PickCount} picks, best first, each a different id from CANDIDATES. Write the reply and reasons in the language the viewer writes in.");
            sb.AppendLine();
            sb.AppendLine("THE VIEWER'S RATINGS:");
            if (profile.Rated.Count == 0) sb.AppendLine("(none yet)");
            foreach (var (id, (stars, review)) in profile.Rated.OrderByDescending(r => r.Value.Stars))
            {
                var name = Name(id);
                if (name == null) continue;
                sb.Append($"- {name}: {stars}/5");
                if (!string.IsNullOrWhiteSpace(review)) sb.Append($" - \"{Trim(review, 200)}\"");
                sb.AppendLine();
            }
            AppendList(sb, "SEEN (not rated):", profile.Seen.Where(id => !profile.Rated.ContainsKey(id)).Select(Name));
            AppendList(sb, "ON THEIR LIST (they wanted to watch these):", profile.Watchlist.Select(Name));
            AppendList(sb, "PART-WAY THROUGH:", profile.Watching.Select(Name));
            sb.AppendLine();
            sb.AppendLine("CANDIDATES, one per line: id|title|year|kind (m movie, s series, o other)|genres|IMDb|RT%|minutes");
            foreach (var c in candidates.OrderBy(c => c.Id))
            {
                sb.Append(c.Id).Append('|').Append(c.Title)
                  .Append('|').Append(c.Year?.ToString() ?? "")
                  .Append('|').Append(c.Kind switch { "movie" => "m", "series" => "s", _ => "o" })
                  .Append('|').Append(string.Join(",", c.Genres.Take(3)))
                  .Append('|').Append(c.ImdbRating?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "")
                  .Append('|').Append(c.RottenTomatoes?.ToString() ?? "")
                  .Append('|').Append(c.RuntimeMinutes?.ToString() ?? "")
                  .AppendLine();
            }
            return sb.ToString();
        }

        private static void AppendList(StringBuilder sb, string heading, IEnumerable<string> names)
        {
            var list = names.Where(n => n != null).Distinct().Take(60).ToList();
            if (list.Count == 0) return;
            sb.AppendLine(heading);
            foreach (var n in list) sb.AppendLine("- " + n);
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "...";

        /// <summary>The model's JSON (tolerating text or a code fence around it); nothing usable answers empty.</summary>
        internal static (string Reply, List<AdvisorPick> Picks) Parse(string text)
        {
            var picks = new List<AdvisorPick>();
            if (string.IsNullOrWhiteSpace(text)) return (null, picks);
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return (null, picks);
            try
            {
                using var doc = JsonDocument.Parse(text[start..(end + 1)]);
                var root = doc.RootElement;
                var reply = root.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                if (root.TryGetProperty("picks", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idEl)) continue;
                        var id = idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var n) ? n
                            : idEl.ValueKind == JsonValueKind.String && int.TryParse(idEl.GetString(), out var m) ? m : 0;
                        if (id <= 0) continue;
                        var reason = item.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;
                        picks.Add(new AdvisorPick { TitleId = id, Reason = reason == null ? null : Trim(reason, 300) });
                    }
                }
                return (reply == null ? null : Trim(reply, 600), picks);
            }
            catch (JsonException)
            {
                return (null, picks);
            }
        }
    }
}
