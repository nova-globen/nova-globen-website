using System.Net;
using Microsoft.Extensions.Logging;

namespace Fravashi.Broker.Tests;

/// <summary>A controllable clock for deterministic TTL / window tests.</summary>
public sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Captures the outbound request and returns a canned response, for forwarder tests.</summary>
public sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>An <see cref="ILogger{T}"/> that records rendered messages, for audit-shape tests.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
