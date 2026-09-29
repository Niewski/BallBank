using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class DiscordWebhookTests
{
    private static readonly string[] Hosts = ["discord.invalid", "ptb.discord.invalid"];

    [Theory]
    [InlineData("https://discord.invalid/api/webhooks/100000000000000001/abcDEF-_123")]
    [InlineData("https://ptb.discord.invalid/api/webhooks/100000000000000001/abcDEF-_123")]
    [InlineData("  https://discord.invalid/api/webhooks/100000000000000001/abcDEF-_123  ")]
    [InlineData("HTTPS://Discord.INVALID/api/webhooks/100000000000000001/abcDEF-_123")]
    public void A_webhook_on_one_of_the_hosts_is_accepted(string url) =>
        DiscordWebhook.Accept(url, Hosts).ShouldEndWith("/api/webhooks/100000000000000001/abcDEF-_123");

    [Fact]
    public void The_accepted_url_is_normalised_to_scheme_host_and_path() =>
        DiscordWebhook.Accept("https://Discord.invalid/api/webhooks/100000000000000001/abc?wait=true&thread_id=5#frag", Hosts)
            .ShouldBe("https://discord.invalid/api/webhooks/100000000000000001/abc");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/api/webhooks/100000000000000001/abc")]
    [InlineData("http://discord.invalid/api/webhooks/100000000000000001/abc")]
    [InlineData("https://example.com/api/webhooks/100000000000000001/abc")]
    [InlineData("https://discord.invalid.example.com/api/webhooks/100000000000000001/abc")]
    [InlineData("https://user:pass@discord.invalid/api/webhooks/100000000000000001/abc")]
    [InlineData("https://discord.invalid:8443/api/webhooks/100000000000000001/abc")]
    [InlineData("https://discord.invalid/api/webhooks/100000000000000001")]
    [InlineData("https://discord.invalid/api/webhooks/not-a-number/abc")]
    [InlineData("https://discord.invalid/api/webhooks/100000000000000001/abc/extra")]
    [InlineData("https://discord.invalid/somewhere/else")]
    public void Anything_else_is_refused_with_a_message_that_says_where_to_find_a_webhook(string? url) =>
        Should.Throw<DomainException>(() => DiscordWebhook.Accept(url, Hosts))
            .Message.ShouldContain("Discord webhook");

    [Fact]
    public void The_tail_is_the_last_four_characters() =>
        DiscordWebhook.Tail("https://discord.invalid/api/webhooks/100000000000000001/abcDEF-_123").ShouldBe("_123");
}
