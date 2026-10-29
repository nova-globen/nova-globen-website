using System.Net;

namespace Fravashi.Broker.Tests;

/// <summary>A stub IdP: routes by URL + bearer to a scripted response and counts the calls.</summary>
internal sealed class StubIdpHandler : HttpMessageHandler
{
    public Func<string, string?, HttpResponseMessage> Responder { get; set; } =
        (_, _) => Status(HttpStatusCode.Unauthorized);

    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        var url = request.RequestUri!.ToString();
        var bearer = request.Headers.Authorization?.Parameter;
        return Task.FromResult(Responder(url, bearer));
    }

    public static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);
}
