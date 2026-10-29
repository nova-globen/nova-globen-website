using Fravashi.Broker.Audit;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using Fravashi.Broker.Endpoints;
using Fravashi.Broker.Forwarding;
using Fravashi.Broker.Identity;
using Fravashi.Broker.PromptAudit;
using Fravashi.Broker.RateLimiting;
using Fravashi.Broker.Revocation;
using Fravashi.Broker.Tokens;
using Microsoft.Extensions.Options;
using Stomme.Infrastructure;

namespace Fravashi.Broker;

/// <summary>
/// The broker's HTTP surface on the host: its API under <see cref="BrokerRoutes.Api"/>, its liveness at
/// <see cref="BrokerRoutes.Health"/>, and its update channel under <see cref="BrokerRoutes.Updates"/>. Mapped by the
/// host on every install and switched off with the module (<c>module.fravashi.enabled=false</c>).
/// <para>
/// The provider never sees the host's prefix: the forward path is the request's path below
/// <see cref="BrokerRoutes.Api"/> (<see cref="BrokerRoutes.UpstreamPath"/>). Nothing the broker sends back names one of
/// its own paths: it sets no cookie, issues no redirect from its API and writes no link. The API is cookie-less and
/// authorises every call itself (a bearer token, or an identity proof for minting and the operator calls).
/// </para>
/// </summary>
public sealed class BrokerEndpoints : IModuleHttpEndpointContributor
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Liveness: 200 while the broker serves, 503 while its deploy-time configuration keeps it from serving.
        // Reveals nothing about the secrets or the configuration.
        endpoints.MapGet(BrokerRoutes.Health, (BrokerState state) => state.Ready
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable));

        // The update channel: every file is a redirect to its release asset, the manifest and its signature alike -
        // the newest release's (updates/<file>, updates/latest/<file>) or a named version's (updates/<version>/<file>).
        string[] reads = [HttpMethods.Get, HttpMethods.Head];
        endpoints.MapMethods(BrokerRoutes.Updates + "/{name}", reads, (string name, IConfiguration configuration) =>
            Update(configuration, null, name));
        endpoints.MapMethods(BrokerRoutes.Updates + "/{version}/{name}", reads, (string version, string name, IConfiguration configuration) =>
            Update(configuration, version, name));

        // While the broker cannot serve, every call to these routes is answered 503 before it reaches them
        // (BrokerModule.Configure): their parameters need the secrets, and are bound before a filter would run.
        var api = endpoints.MapGroup(BrokerRoutes.Api);

        // Mint (server side): validate the user identity, then mint a short-lived HMAC token.
        api.MapPost("/broker/token", async (
            MintRequest request,
            HttpContext http,
            IIdentityValidator identity,
            TokenService tokens,
            IAuditSink audit,
            IOptions<BrokerServiceOptions> opts,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var scope = string.IsNullOrWhiteSpace(request.Scope) ? opts.Value.TokenScope : request.Scope!;
            var validated = await identity.ValidateAsync(request.UserId ?? string.Empty, request.IdentityProof, ct);
            if (validated is null)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "mint", request.UserId, null, null, null,
                    AuditRecord.OutcomeDenied, "identity_rejected"));
                return Results.Json(new { error = "identity_rejected" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // Authenticated is not entitled: entitlement is the second gate, by the
            // allowlist or by a role the IdP vouches for, and the audit records the canonical id it refused so an
            // operator can entitle it deliberately.
            if (opts.Value.Identity.Providers.Count > 0
                && MintPolicy.Refusal(validated, opts.Value.Identity) is { } refusal)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "mint", validated.CanonicalId, null, null, null,
                    AuditRecord.OutcomeDenied, refusal));
                // The refused id rides in the body as well as in the audit. It is not a secret and it is the caller's
                // own: they authenticated as it a moment ago. What it saves is an operator's trip to the container
                // console to read the audit line, which on a containerised host may be the only place the log can be read, just
                // to learn which oidc:<sub> to entitle.
                return Results.Json(
                    new { error = refusal, subject = validated.CanonicalId }, statusCode: StatusCodes.Status403Forbidden);
            }

            // A suspended user is not served, and a new token is the first thing they would ask for.
            if (http.RequestServices.GetRequiredService<ForwardContainment>().Suspended(validated.CanonicalId) is { } stopped)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "mint", validated.CanonicalId, null, null, null,
                    AuditRecord.OutcomeDenied, stopped.Reason));
                return Results.Json(stopped.Body, statusCode: stopped.Status);
            }

            var (token, claims) = tokens.Mint(validated.CanonicalId, scope, opts.Value.TokenTtl);
            audit.Write(new AuditRecord(clock.GetUtcNow(), "mint", claims.Subject, claims.TokenId, null, null,
                AuditRecord.OutcomeAllowed, "minted"));
            var models = http.RequestServices.GetRequiredService<ModelPolicy>().Advertised;
            return Results.Ok(new MintResponse(token, claims.TokenId, claims.ExpiresAt, claims.Subject, models));
        });

        // Revoke (self-service). Requires a valid bearer token.
        api.MapPost("/broker/revoke", (
            RevokeRequest? request,
            HttpContext http,
            TokenService tokens,
            IRevocationStore revocations,
            IAuditSink audit,
            IOptions<BrokerServiceOptions> opts,
            TimeProvider clock) =>
        {
            var result = tokens.Validate(ReadBearer(http), opts.Value.TokenScope);
            if (!result.IsValid)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "revoke", null, null, null, null,
                    AuditRecord.OutcomeDenied, result.Reason));
                return Results.Json(new { error = result.Reason }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var claims = result.Token!;
            if (request?.AllMyTokens == true)
            {
                revocations.RevokeUser(claims.Subject, clock.GetUtcNow());
            }
            else
            {
                revocations.RevokeToken(claims.TokenId);
            }

            audit.Write(new AuditRecord(clock.GetUtcNow(), "revoke", claims.Subject, claims.TokenId, null, null,
                AuditRecord.OutcomeAllowed, request?.AllMyTokens == true ? "revoked_user" : "revoked_token"));
            return Results.Ok(new { revoked = true });
        });

        // Forward (Anthropic-compatible). Validate the bearer, enforce rate limit + quota, then forward with the org key
        // added server-side. Streams the provider response back (SSE passthrough).
        Func<HttpContext, Task> forward = async http =>
        {
            // The module refuses an unconfigured broker before the endpoint runs; the forward asks again before it resolves
            // anything that needs the secrets, whichever way the framework binds this delegate.
            if (!http.RequestServices.GetRequiredService<BrokerState>().Ready)
            {
                await Results.Json(new { error = "broker_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable)
                    .ExecuteAsync(http);
                return;
            }

            var tokens = http.RequestServices.GetRequiredService<TokenService>();
            var meter = http.RequestServices.GetRequiredService<IUsageMeter>();
            var forwarder = http.RequestServices.GetRequiredService<IUpstreamForwarder>();
            var audit = http.RequestServices.GetRequiredService<IAuditSink>();
            var clock = http.RequestServices.GetRequiredService<TimeProvider>();
            var opts = http.RequestServices.GetRequiredService<IOptions<BrokerServiceOptions>>().Value;

            var validation = tokens.Validate(ReadBearer(http), opts.TokenScope);
            if (!validation.IsValid)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "forward", null, null, null, null,
                    AuditRecord.OutcomeDenied, validation.Reason));
                await Results.Json(new { error = validation.Reason }, statusCode: StatusCodes.Status401Unauthorized)
                    .ExecuteAsync(http);
                return;
            }

            var claims = validation.Token!;
            var containment = http.RequestServices.GetRequiredService<ForwardContainment>();

            var usage = meter.TryConsume(claims.Subject);
            if (!usage.Allowed)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "forward", claims.Subject, claims.TokenId, null, null,
                    AuditRecord.OutcomeDenied, usage.Reason));
                containment.Refused(claims.Subject, usage.Reason);
                await Results.Json(new { error = usage.Reason }, statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(http);
                return;
            }

            var body = await ReadBodyAsync(http);
            var model = MessagesRequest.TryReadModel(body);

            // Decided before the org key is reached: is this user served at all, is this a model the
            // grant covers, and is there budget. Every refusal it makes counts toward a suspension.
            if (containment.Before(claims.Subject, model) is { } refused)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "forward", claims.Subject, claims.TokenId, model, null,
                    AuditRecord.OutcomeDenied, refused.Reason));
                await Results.Json(refused.Body, statusCode: refused.Status).ExecuteAsync(http);
                return;
            }

            // The prompts are judged after every cheaper refusal (a request the grant or the budget refuses
            // anyway is not worth a call) and before the org key is reached. A request judged too risky, or one whose
            // prompts cannot be read, counts toward a suspension like any other refusal; one refused only because the
            // inspection service did not answer does not, because that is not the user's doing.
            var path = BrokerRoutes.UpstreamPath(http.Request.Path.Value) ?? ForwardContainment.MessagesPath;
            var inspection = await http.RequestServices.GetRequiredService<PromptAuditor>()
                .AuditAsync(path, body, http.RequestAborted);
            if (PromptAuditReply.For(inspection) is { } stopped)
            {
                audit.Write(new AuditRecord(clock.GetUtcNow(), "forward", claims.Subject, claims.TokenId, model, null,
                    AuditRecord.OutcomeDenied, stopped.Reason, null, inspection.Reason, inspection.Assessment?.Risk,
                    Flags(inspection)));
                if (inspection.Decision is not PromptAuditDecision.Unavailable)
                {
                    containment.Refused(claims.Subject, stopped.Reason);
                }

                await Results.Json(stopped.Body, statusCode: stopped.Status).ExecuteAsync(http);
                return;
            }

            using var upstream = await forwarder.ForwardAsync(
                new ForwardedCall(
                    path,
                    http.Request.QueryString.Value,
                    body,
                    http.Request.ContentType,
                    ForwardedCall.PassedThrough(http.Request.Headers.Select(h => (h.Key, h.Value.ToString())))),
                http.RequestAborted);

            // The answer is relayed as it arrives and its usage figures are read out of the same bytes on the way past.
            // The charge and the audit line come AFTER it, because the figure is only known once the answer is over; a
            // client that hangs up mid-answer is still charged for what the provider had reported by then.
            var scanner = new UsageScanner();
            try
            {
                await WriteUpstreamAsync(http, upstream, scanner);
            }
            finally
            {
                var charged = containment.After(claims.Subject, path, (int)upstream.StatusCode, scanner.Usage);
                audit.Write(new AuditRecord(clock.GetUtcNow(), "forward", claims.Subject, claims.TokenId, model, 1,
                    AuditRecord.OutcomeAllowed, ((int)upstream.StatusCode).ToString(), charged,
                    inspection.Decision == PromptAuditDecision.NotInspected ? null : inspection.Reason,
                    inspection.Assessment?.Risk, Flags(inspection)));
            }
        };

        // What the broker has been doing, for an operator. The same identity proof minting takes, so an
        // operator needs no second credential and nothing here is protected by a shared secret. It answers with
        // figures and reasons only: no part of a conversation is kept anywhere, so no part of one can be read back.
        api.MapPost("/broker/usage", async (
            OperatorRequest request,
            IIdentityValidator identity,
            Suspensions suspensions,
            ITokenBudget budget,
            IAuditSink audit,
            IOptions<BrokerServiceOptions> opts,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var who = await OperatorOrNull(request, identity, opts.Value, audit, clock, ct);
            if (who is null)
            {
                return Results.Json(new { error = MintPolicy.NotAnOperatorReason }, statusCode: StatusCodes.Status403Forbidden);
            }

            var suspended = suspensions.All()
                .Select(s => new { user = s.User, since = s.Suspension.Since, refusals = s.Suspension.Refusals, reason = s.Suspension.Reason })
                .ToList();

            var organisation = budget.OrganisationUsed();
            var weights = opts.Value.TokenWeights;
            return Results.Ok(new
            {
                asOf = clock.GetUtcNow(),
                suspended,
                // the budgets count weighted units; the raw tokens behind them, as the provider counted, beside
                weights = new
                {
                    input = weights.Input, cacheWrite = weights.CacheWrite, cacheRead = weights.CacheRead, output = weights.Output,
                    longContextThreshold = weights.LongContextThreshold, longContextMultiplier = weights.LongContextMultiplier,
                },
                organisation = new
                {
                    unitsUsed = organisation.Units,
                    tokensUsed = organisation.RawTokens,
                    unitsPerDay = opts.Value.OrganisationTokensPerDay,
                },
                // A named user's own figures, when one was asked about.
                user = string.IsNullOrWhiteSpace(request.User) ? null : new
                {
                    id = request.User,
                    unitsUsed = budget.UsedBy(request.User!),
                    tokensUsed = budget.RawUsedBy(request.User!),
                    unitsPerWindow = opts.Value.QuotaTokens,
                    refusals = suspensions.RefusalsOf(request.User!),
                    suspendedSince = suspensions.Of(request.User!)?.Since,
                },
            });
        });

        // Lifting a suspension, which is the one thing about it that is not automatic.
        api.MapPost("/broker/lift", async (
            OperatorRequest request,
            IIdentityValidator identity,
            Suspensions suspensions,
            IAuditSink audit,
            IOptions<BrokerServiceOptions> opts,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var who = await OperatorOrNull(request, identity, opts.Value, audit, clock, ct);
            if (who is null)
            {
                return Results.Json(new { error = MintPolicy.NotAnOperatorReason }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(request.User))
            {
                return Results.Json(new { error = "no_user" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var lifted = suspensions.Lift(request.User!);
            audit.Write(new AuditRecord(clock.GetUtcNow(), "lift", who, null, null, null,
                lifted ? AuditRecord.OutcomeAllowed : AuditRecord.OutcomeDenied, lifted ? "lifted:" + request.User : "not_suspended"));
            return Results.Ok(new { lifted, user = request.User });
        });

        api.MapPost("/v1/messages", forward);
        api.MapPost("/v1/messages/count_tokens", forward);
        api.MapPost("/v1/{**rest}", forward);
    }


    /// <summary>
    /// The operator's canonical id when this request proves one, or null. Authentication first, then entitlement,
    /// exactly as minting does it, and a refusal is audited with the id it refused so an operator can be named
    /// deliberately rather than by guesswork.
    /// </summary>
    private static async Task<string?> OperatorOrNull(
        OperatorRequest request,
        IIdentityValidator identity,
        BrokerServiceOptions options,
        IAuditSink audit,
        TimeProvider clock,
        CancellationToken ct)
    {
        var validated = await identity.ValidateAsync(request.UserId ?? string.Empty, request.IdentityProof, ct);
        if (validated is null)
        {
            audit.Write(new AuditRecord(clock.GetUtcNow(), "operator", request.UserId, null, null, null,
                AuditRecord.OutcomeDenied, "identity_rejected"));
            return null;
        }

        if (!MintPolicy.IsOperator(validated, options.Identity))
        {
            audit.Write(new AuditRecord(clock.GetUtcNow(), "operator", validated.CanonicalId, null, null, null,
                AuditRecord.OutcomeDenied, MintPolicy.NotAnOperatorReason));
            return null;
        }

        return validated.CanonicalId;
    }

    /// <summary>The redirect to an update file's release asset, or 404 for an address the channel refuses.</summary>
    private static IResult Update(IConfiguration configuration, string? version, string name) =>
        BrokerRoutes.UpdateTarget(configuration[BrokerRoutes.ReleasesUrlKey], version, name) is { } target
            ? Results.Redirect(target)
            : Results.NotFound();

    private static string? Flags(PromptAuditOutcome inspection) =>
        inspection.Assessment is { Flagged.Count: > 0 } a ? string.Join(",", a.Flagged) : null;

    private static string? ReadBearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContext http)
    {
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, http.RequestAborted);
        return buffer.ToArray();
    }

    private static async Task WriteUpstreamAsync(HttpContext http, HttpResponseMessage upstream, UsageScanner scanner)
    {
        http.Response.StatusCode = (int)upstream.StatusCode;
        if (upstream.Content.Headers.ContentType is { } contentType)
        {
            http.Response.ContentType = contentType.ToString();
        }

        // The org key is never on the response - it went upstream only. Stream the provider body straight back
        // (SSE passthrough): copy progressively so streamed events flush to the client.
        await using var stream = await upstream.Content.ReadAsStreamAsync(http.RequestAborted);
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, http.RequestAborted)) > 0)
        {
            // The same bytes go to the client and past the usage reader, in that order, and each chunk is flushed,
            // so a streamed answer reaches the client exactly as promptly as it did when this was a plain copy.
            await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
            scanner.Feed(buffer.AsSpan(0, read));
        }
    }
}
