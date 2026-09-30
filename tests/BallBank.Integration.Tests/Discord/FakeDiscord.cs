using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;

namespace BallBank.Integration.Tests.Discord;

/// <summary>
/// Discord faked at the message-handler level, the way <c>FakeSleeper</c> fakes Sleeper: webhooks live on
/// an invalid host, answer <c>404</c> (Discord's "Unknown Webhook") until a test registers them, and
/// every message they accept is kept, so no call ever leaves the machine. Every test makes webhooks of its
/// own, so what one posts is never mistaken for what another expects.
/// </summary>
public sealed class FakeDiscord
{
    public const string Host = "discord.invalid";

    private readonly ConcurrentDictionary<string, Func<HttpStatusCode>> _webhooks = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PostedMessage>> _posted = new();
    private readonly ConcurrentDictionary<string, int> _attempts = new();

    /// <summary>A webhook nobody has used, that accepts what is posted to it.</summary>
    public Webhook NewWebhook()
    {
        var url = $"https://{Host}/api/webhooks/{Random.Shared.NextInt64(100000000000000000, 999999999999999999)}/fake-{Guid.NewGuid():N}";
        _webhooks[url] = () => HttpStatusCode.NoContent;
        return new Webhook(this, url);
    }

    /// <summary>A webhook Discord never heard of, or one whose channel was deleted.</summary>
    public Webhook UnknownWebhook()
    {
        var webhook = NewWebhook();
        webhook.RefusesWith(HttpStatusCode.NotFound);
        return webhook;
    }

    /// <summary>A fresh handler over this fake; the HTTP client factory disposes handlers as it rotates them.</summary>
    public HttpMessageHandler Handler() => new DiscordHandler(this);

    public sealed class Webhook(FakeDiscord discord, string url)
    {
        public string Url { get; } = url;

        /// <summary>The last characters of the URL, which is all the API ever says of it.</summary>
        public string Tail => Url[^4..];

        /// <summary>What Discord accepted, oldest first.</summary>
        public IReadOnlyList<PostedMessage> Posted =>
            discord._posted.TryGetValue(Url, out var posted) ? [.. posted] : [];

        /// <summary>How many times something was sent, accepted or not.</summary>
        public int Attempts => discord._attempts.GetValueOrDefault(Url);

        public void RefusesWith(HttpStatusCode status) => discord._webhooks[Url] = () => status;

        public void Accepts() => discord._webhooks[Url] = () => HttpStatusCode.NoContent;

        /// <summary>Waits for Discord to have accepted this many messages; the API posts after it has answered.</summary>
        public async Task<IReadOnlyList<PostedMessage>> WaitForPosts(int count, TimeSpan? within = null)
        {
            var deadline = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(30));
            while (Posted.Count < count && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Posted.Count.ShouldBeGreaterThanOrEqualTo(count, $"Discord was expected to accept {count} messages, and accepted {Posted.Count}.");
            return Posted;
        }

        /// <summary>Waits for this many attempts, refused ones included.</summary>
        public async Task WaitForAttempts(int count, TimeSpan? within = null)
        {
            var deadline = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(30));
            while (Attempts < count && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Attempts.ShouldBeGreaterThanOrEqualTo(count);
        }
    }

    /// <summary>One message Discord accepted: its text, and the JSON it arrived in.</summary>
    public sealed record PostedMessage(string Content, JsonObject Payload);

    private sealed class DiscordHandler(FakeDiscord discord) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var uri = request.RequestUri!;
            if (uri.Host != Host)
            {
                throw new InvalidOperationException($"The fake Discord was asked for {uri}, which is not a webhook on {Host}.");
            }

            var url = uri.GetLeftPart(UriPartial.Path);
            discord._attempts.AddOrUpdate(url, 1, (_, attempts) => attempts + 1);

            var status = discord._webhooks.TryGetValue(url, out var respond) ? respond() : HttpStatusCode.NotFound;
            if (status is HttpStatusCode.NoContent)
            {
                var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellation))!.AsObject();
                discord._posted.GetOrAdd(url, _ => new ConcurrentQueue<PostedMessage>())
                    .Enqueue(new PostedMessage((string)payload["content"]!, payload));
            }

            return new HttpResponseMessage(status);
        }
    }
}
