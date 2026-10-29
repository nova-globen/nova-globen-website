---

title: "SDLC in the AI Era with Spec-Driven Development"
slug: "ai-sdlc-spec-driven-development"
description: "Learn how spec-driven development turns AI from a coding shortcut into a governed delivery system for reliable, traceable .NET and Azure software."
date: "2026-07-13"
tags: [ai, delivery]
cover: true
draft: false
---

AI can generate an endpoint, a test fixture or a deployment manifest in seconds. That speed is useful, but it does not solve the expensive part of software delivery: deciding what the system should do, proving that the implementation matches that intent and controlling change across architecture, security and operations. In an AI-assisted SDLC, you need a stronger source of truth than a backlog item or a chat transcript. Spec-driven development gives you that control plane. You express behaviour, contracts, constraints and acceptance criteria in durable specifications, then let engineers and AI agents derive code, tests, documentation and delivery checks from them.

## The specification becomes the delivery control plane

Traditional delivery often treats specifications as temporary input. A product owner writes a story, an engineer interprets it, and the details disperse into code, tests, pull-request comments and operational knowledge. AI amplifies the weakness of that model. A model will confidently fill gaps, choose defaults and optimise locally unless you provide explicit boundaries. The resulting code may compile while violating business rules, security assumptions or integration contracts.

Spec-driven development reverses that relationship. The specification remains authoritative throughout the lifecycle. It does not need to be one enormous document; it can be a versioned set of linked artefacts: an OpenAPI contract, domain rules, data schemas, architecture decisions, user-flow states, non-functional requirements and executable acceptance criteria. What matters is that every material implementation decision traces back to an agreed statement of intent.

For a .NET and Azure team, the specification can function as an engineering control plane. You can use it to constrain code generation, review proposed changes, generate contract tests, validate infrastructure and explain production behaviour. The code is still important, but it becomes one projection of the system rather than the only reliable description of it. That distinction makes AI assistance safer because generated output can be evaluated against stable, reviewable constraints.

## Rebuild the lifecycle around executable intent

You do not need to replace the SDLC with an AI-specific methodology. You need to tighten the transitions between its stages. Discovery should produce measurable outcomes and explicit risks. Requirements should separate business behaviour from implementation preference. Architecture should record the constraints that generated code must respect. Implementation should begin only when the relevant specification is testable. Testing should verify the specification rather than merely exercising the code that happened to be written.

A practical flow looks like this: define the problem, elicit requirements, write the specification, validate it with stakeholders, derive the architecture, implement against the specification, derive tests from the same source, integrate continuously, release with observable controls and feed production evidence back into the next specification revision. AI can assist at every step, but it should not silently own any approval boundary. People remain accountable for intent, risk acceptance and trade-offs.

<figure style="margin: 2rem 0; text-align: center;">
  <img src="/blog/ai-sdlc-spec-driven-development/sdlc_full_rewritten_final.svg" alt="SDLC in the AI Era with Spec-Driven Development — full lifecycle diagram" loading="lazy" style="max-width: 100%; height: auto;">
  <figcaption style="margin-top: 0.75rem; font-size: 0.875rem; color: var(--muted, #6b7280);"><strong>Image 2</strong> — SDLC in the AI Era: Spec-Driven Development lifecycle flow. &copy; 2026 Nova Globen AB. All rights reserved.</figcaption>
</figure>

This changes the meaning of common delivery gates. Your Definition of Ready should require unambiguous acceptance criteria, identified data ownership, security classification, failure behaviour and measurable non-functional targets. Your Definition of Done should require traceability from requirement to specification, design, code, tests and telemetry. A pull request that adds working code without updating the governing specification is incomplete, just as a database migration without a rollback strategy is incomplete.

The lifecycle also becomes more iterative. Production telemetry can reveal that a latency target was unrealistic, a workflow causes abandonment or an integration fails under a particular tenant configuration. You should convert that evidence into a specification change before asking an agent to modify the implementation. That keeps learning explicit and prevents production behaviour from drifting away from documented intent.

## Turn specifications into code, tests and quality gates

A useful specification is precise enough to be checked but small enough to review. Start with the highest-value slice of behaviour rather than trying to formalise the entire platform. For an order-submission capability, you might define the API contract, validation boundaries, accepted response, idempotency expectations, authorisation policy and service-level target. You can then provide the same artefacts to an engineer, a coding agent and the CI pipeline.

The following example creates a minimal API whose behaviour is explicit and testable. Create the project with `dotnet new web -n OrderApi -f net10.0`, then replace `Program.cs` with this file:

```csharp
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapPost("/orders", (SubmitOrder request) =>
{
    var validationResults = new List<ValidationResult>();
    var context = new ValidationContext(request);

    if (!Validator.TryValidateObject(
            request,
            context,
            validationResults,
            validateAllProperties: true))
    {
        var errors = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty("request")
                .Select(member => new { member, result.ErrorMessage }))
            .GroupBy(item => item.member)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item =>
                    item.ErrorMessage ?? "Invalid value").ToArray());

        return Results.ValidationProblem(errors);
    }

    var orderId = Guid.NewGuid();
    var response = new OrderAccepted(orderId, "accepted");

    return Results.Accepted($"/orders/{orderId}", response);
});

app.Run();

public sealed record SubmitOrder(
    [property: Required]
    [property: RegularExpression("^[A-Z0-9-]{3,32}$")]
    string ProductCode,

    [property: Range(1, 100)]
    int Quantity);

public sealed record OrderAccepted(Guid OrderId, string Status);

public partial class Program;
```

The implementation is deliberately small. The important point is that the rules are visible: product codes use a defined format, quantity has a bounded range, invalid input returns a validation problem and valid input returns `202 Accepted` with a resource location. An AI agent should not invent different limits or response semantics because the governing specification and acceptance criteria have already fixed them.

Now create executable evidence. From the solution directory, run the following commands:

```bash
dotnet new xunit -n OrderApi.Tests -f net10.0
dotnet add OrderApi.Tests reference OrderApi
dotnet add OrderApi.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add OrderApi.Tests package FluentAssertions
```

Replace the generated test file with this contract-focused test suite:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class OrderApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrderApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Submit_valid_order_returns_accepted_resource()
    {
        var response = await _client.PostAsJsonAsync(
            "/orders",
            new { productCode = "AZURE-42", quantity = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!
            .ToString()
            .Should()
            .StartWith("/orders/");

        var body =
            await response.Content.ReadFromJsonAsync<OrderAccepted>();

        body!.Status.Should().Be("accepted");
        body.OrderId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("x", 1)]
    [InlineData("VALID-1", 0)]
    [InlineData("VALID-1", 101)]
    public async Task Submit_invalid_order_returns_bad_request(
        string productCode,
        int quantity)
    {
        var response = await _client.PostAsJsonAsync(
            "/orders",
            new { productCode, quantity });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

These tests are more than regression protection. They are executable acceptance criteria. You can require an AI-generated change to preserve them, extend them when the specification changes and reject code that changes behaviour without an approved contract update. The same pattern applies to Azure infrastructure: policy-as-code can enforce network exposure, managed identity, diagnostic settings, data residency and recovery requirements derived from non-functional specifications.

## Govern AI-generated change without slowing delivery

AI governance should focus on evidence and authority, not on banning tools or adding ceremonial approval. Decide which artefacts an agent may propose, which it may modify automatically and which require human approval. An agent can usually generate scaffolding, mappings, tests, documentation and low-risk refactoring. It should not independently redefine a public API, weaken an authorisation policy, change a retention rule or accept a production risk.

You should also preserve the generation context. Record the specification version, model or agent, relevant prompts, changed files and validation results as part of the pull request or build metadata. You do not need to archive every conversational token, but you do need enough provenance to explain why a change exists and which constraints governed it. This becomes particularly important when several agents work across application code, Bicep or Terraform, pipelines and operational documentation.

Your CI pipeline should validate relationships, not only files. Check that every changed requirement has corresponding acceptance criteria. Validate OpenAPI and JSON Schema documents. Run architectural tests that prohibit forbidden dependencies. Compare infrastructure against Azure Policy. Run security scanning, unit tests, integration tests and performance thresholds. Finally, verify that telemetry exists for the outcomes and failure modes declared in the specification.

Metrics should reflect delivery quality rather than raw AI output. Track requirement-to-test traceability, escaped-defect rate, change-failure rate, lead time, mean time to recovery, specification churn and the proportion of generated changes accepted without substantial rework. Lines of generated code and prompt counts are activity measures; they tell you little about whether you built the right system.

## Takeaway: build the right thing, then keep proving it

AI makes implementation cheaper, which increases the relative cost of ambiguity. When code can be produced quickly, unclear intent creates more wrong code at greater speed. Spec-driven development counters that risk by making agreed behaviour, constraints and evidence the centre of the lifecycle. It gives your engineers a shared language and gives AI agents a bounded problem they can solve reliably.

You should not aim for perfect formal specifications everywhere. Apply precision where mistakes are expensive: public contracts, financial rules, identity and access, data handling, integrations, deployment safety and operational objectives. Keep the specification versioned, reviewable and connected to executable checks. Then use AI aggressively inside those boundaries to accelerate analysis, implementation, testing, documentation and incident learning.

The operating principle is straightforward: build the right thing, build it right and keep proving that it remains right. In the AI era, the specification is not paperwork before development. It is the durable mechanism that connects business intent to architecture, code, tests, deployment and production evidence.
