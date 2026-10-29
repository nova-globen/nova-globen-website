using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Fravashi.Broker.Audit;

/// <summary>
/// The default <see cref="IAuditSink"/>: emits each record as a single structured JSON log line under a stable
/// event id, so a customer's log pipeline can parse and attribute it. Serializes only the
/// <see cref="AuditRecord"/> fields - which by construction contain no secrets.
/// </summary>
public sealed class LoggingAuditSink(ILogger<LoggingAuditSink> logger) : IAuditSink
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly EventId AuditEvent = new(1000, "BrokerAudit");

    public void Write(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        logger.LogInformation(AuditEvent, "broker-audit {AuditJson}", JsonSerializer.Serialize(record, Json));
    }
}
