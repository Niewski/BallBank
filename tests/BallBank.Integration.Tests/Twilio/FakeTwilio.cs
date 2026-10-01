using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace BallBank.Integration.Tests.Twilio;

/// <summary>Twilio faked at the message-handler level, like <see cref="Discord.FakeDiscord"/>; texts are kept per league, named by their status callback.</summary>
public sealed class FakeTwilio
{
    public const string Host = "api.twilio.com";
    public const string StatusCallbackBaseUrl = "https://ballbank.invalid";

    // Twilio's account SIDs are "AC" and 32 hex digits.
    public string AccountSid { get; } = $"AC{Guid.NewGuid():N}";

    public string AuthToken { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The toll-free number every text is sent from.</summary>
    public string FromNumber => "+15550100100";

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<SentText>> _accepted = new();
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();
    private readonly ConcurrentDictionary<Guid, HttpStatusCode> _refusals = new();

    /// <summary>A fresh handler over this fake; the HTTP client factory disposes handlers as it rotates them.</summary>
    public HttpMessageHandler Handler() => new TwilioHandler(this);

    /// <summary>What Twilio accepted for this league, oldest first.</summary>
    public IReadOnlyList<SentText> Sent(Guid leagueId) =>
        _accepted.TryGetValue(leagueId, out var accepted) ? [.. accepted] : [];

    /// <summary>What Twilio accepted for this league's member at <paramref name="phone"/>, oldest first.</summary>
    public IReadOnlyList<SentText> SentTo(Guid leagueId, string phone) => [.. Sent(leagueId).Where(text => text.To == phone)];

    /// <summary>How many times a send was made for this league, accepted or not.</summary>
    public int Attempts(Guid leagueId) => _attempts.GetValueOrDefault(leagueId);

    public void RefusesWith(Guid leagueId, HttpStatusCode status) => _refusals[leagueId] = status;

    public void Accepts(Guid leagueId) => _refusals.TryRemove(leagueId, out _);

    /// <summary>Waits for Twilio to have accepted this many texts for the league; the API sends after it has decided to.</summary>
    public async Task<IReadOnlyList<SentText>> WaitForTexts(Guid leagueId, int count, TimeSpan? within = null)
    {
        var deadline = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(30));
        while (Sent(leagueId).Count < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Sent(leagueId).Count.ShouldBeGreaterThanOrEqualTo(count, $"Twilio was expected to accept {count} texts, and accepted {Sent(leagueId).Count}.");
        return Sent(leagueId);
    }

    /// <summary>Waits for this many sends for the league, refused ones included.</summary>
    public async Task WaitForAttempts(Guid leagueId, int count, TimeSpan? within = null)
    {
        var deadline = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(30));
        while (Attempts(leagueId) < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Attempts(leagueId).ShouldBeGreaterThanOrEqualTo(count);
    }

    /// <summary>Where Twilio posts a reply to the toll-free number: the address the number is configured with.</summary>
    public static string InboundUrl => $"{StatusCallbackBaseUrl}/webhooks/twilio/inbound";

    /// <summary>Twilio's request signature for a post of <c>form</c> to <c>url</c>, by <c>authToken</c> or the fake's own.</summary>
    public string Sign(string url, IEnumerable<(string Name, string Value)> form, string? authToken = null)
    {
        var data = new StringBuilder(url);
        foreach (var (name, value) in form.OrderBy(parameter => parameter.Name, StringComparer.Ordinal))
        {
            data.Append(name).Append(value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken ?? AuthToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
    }

    /// <summary>Twilio reporting to <paramref name="url"/> as it does: a form post, signed with the account's token.</summary>
    public Task<HttpResponseMessage> Report(HttpClient client, string url, params (string Name, string Value)[] form) =>
        Post(client, url, form, Sign(url, form));

    /// <summary>A reply to the number: <paramref name="body"/>, from <paramref name="from"/>, signed as Twilio signs it.</summary>
    public Task<HttpResponseMessage> Reply(HttpClient client, string from, string body) =>
        Report(client, InboundUrl, ("MessageSid", $"SM{Guid.NewGuid():N}"), ("From", from), ("To", FromNumber), ("Body", body));

    /// <summary>A form post to <paramref name="url"/>'s path and query, with <paramref name="signature"/> as its signature when there is one.</summary>
    public static Task<HttpResponseMessage> Post(
        HttpClient client, string url, IEnumerable<(string Name, string Value)> form, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url).PathAndQuery)
        {
            Content = new FormUrlEncodedContent(form.Select(parameter => KeyValuePair.Create(parameter.Name, parameter.Value))),
        };
        if (signature is not null)
        {
            request.Headers.Add("X-Twilio-Signature", signature);
        }

        return client.SendAsync(request);
    }

    /// <summary>One text Twilio accepted: where it went, what it said, and the callback it was asked to report to.</summary>
    public sealed record SentText(string To, string From, string Body, Uri StatusCallback, Guid LeagueId, string NotificationId);

    private sealed class TwilioHandler(FakeTwilio twilio) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var uri = request.RequestUri!;
            if (uri.Host != Host)
            {
                throw new InvalidOperationException($"The fake Twilio was asked for {uri}, which is not on {Host}.");
            }

            if (request.Method != HttpMethod.Post || uri.AbsolutePath != $"/2010-04-01/Accounts/{twilio.AccountSid}/Messages.json")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellation));
            var callback = new Uri(form["StatusCallback"] ?? throw new InvalidOperationException("The text asked for no status callback."));
            var query = HttpUtility.ParseQueryString(callback.Query);
            var leagueId = Guid.Parse(query["league"]!);
            twilio._attempts.AddOrUpdate(leagueId, 1, (_, attempts) => attempts + 1);

            if (!IsAuthorized(request.Headers.Authorization))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            if (twilio._refusals.TryGetValue(leagueId, out var refusal))
            {
                return new HttpResponseMessage(refusal);
            }

            twilio._accepted.GetOrAdd(leagueId, _ => new ConcurrentQueue<SentText>())
                .Enqueue(new SentText(form["To"]!, form["From"]!, form["Body"]!, callback, leagueId, query["notification"]!));

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent($$"""{"sid":"SM{{Guid.NewGuid():N}}","status":"queued"}""", Encoding.UTF8, "application/json"),
            };
        }

        private bool IsAuthorized(AuthenticationHeaderValue? header) =>
            header is { Scheme: "Basic", Parameter: { } parameter }
            && Encoding.UTF8.GetString(Convert.FromBase64String(parameter)) == $"{twilio.AccountSid}:{twilio.AuthToken}";
    }
}
