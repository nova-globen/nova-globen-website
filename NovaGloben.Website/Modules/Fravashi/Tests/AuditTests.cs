using Fravashi.Broker.Audit;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class AuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Audit_line_captures_attribution_and_outcome()
    {
        var logger = new ListLogger<LoggingAuditSink>();
        var sink = new LoggingAuditSink(logger);

        sink.Write(new AuditRecord(Now, "forward", "alice", "jti-123", "claude-sonnet", 1,
            AuditRecord.OutcomeAllowed, "200"));

        var line = logger.Messages.Should().ContainSingle().Subject;
        line.Should().Contain("alice");        // attribution
        line.Should().Contain("jti-123");      // token id for correlation
        line.Should().Contain("forward");      // action
        line.Should().Contain("claude-sonnet"); // model
        line.Should().Contain("allowed");      // outcome
    }

    [Fact]
    public void Audit_line_contains_no_secrets()
    {
        var logger = new ListLogger<LoggingAuditSink>();
        var sink = new LoggingAuditSink(logger);

        // Even if a caller carelessly passed secret-looking values, the record has no field to hold them -
        // the audit shape is fixed to non-secret fields. Assert the rendered line has none of the secret markers.
        sink.Write(new AuditRecord(Now, "forward", "alice", "jti-123", "claude", 1,
            AuditRecord.OutcomeDenied, "revoked"));

        var line = logger.Messages.Single();
        line.Should().NotContain("sk-ant");                 // no org key
        line.Should().NotContain("BROKER_ORG_API_KEY");
        line.Should().NotContain("signing");                // no signing secret
    }

    [Fact]
    public void Denied_outcome_records_the_reason()
    {
        var logger = new ListLogger<LoggingAuditSink>();
        new LoggingAuditSink(logger).Write(new AuditRecord(Now, "forward", "alice", "jti-1", null, null,
            AuditRecord.OutcomeDenied, "rate_limited"));

        logger.Messages.Single().Should().Contain("rate_limited").And.Contain("denied");
    }
}
