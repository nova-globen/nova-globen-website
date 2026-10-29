namespace Fravashi.Broker.Audit;

/// <summary>Writes an <see cref="AuditRecord"/> to the broker's audit trail. The default sink logs structured JSON.</summary>
public interface IAuditSink
{
    void Write(AuditRecord record);
}
