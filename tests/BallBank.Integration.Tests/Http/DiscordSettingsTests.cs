using System.Net;
using System.Net.Http.Json;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using BallBank.Integration.Tests.Discord;
using Microsoft.AspNetCore.Mvc;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class DiscordSettingsTests(PostgresFixture postgres)
{
    private BallBankApi Api => postgres.Api;

    [Fact]
    public async Task Connecting_saves_the_webhook_and_says_hello_through_it()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.NewWebhook();

        var response = await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: true, PostDigest: false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DiscordSettings>()).ShouldBe(new DiscordSettings(true, webhook.Tail, true, false));
        webhook.Posted.Select(p => p.Content).ShouldBe([NotificationTexts.Hello("Holland Hogs")]);
    }

    [Fact]
    public async Task Reading_says_whether_Discord_is_connected_with_the_tail_of_the_webhook_and_the_flags()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.NewWebhook();

        (await Get(hogs, hogs.Jacob)).ShouldBe(new DiscordSettings(false, null, false, false));

        await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: false, PostDigest: true));

        (await Get(hogs, hogs.Jacob)).ShouldBe(new DiscordSettings(true, webhook.Tail, false, true));
    }

    [Fact]
    public async Task The_full_webhook_url_appears_in_no_response()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.NewWebhook();
        var token = new Uri(webhook.Url).Segments.Last();

        var put = await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: true, PostDigest: true));
        var get = await Api.CreateClientFor(hogs.Jacob).GetAsync(SettingsPath(hogs));

        foreach (var body in new[] { await put.Content.ReadAsStringAsync(), await get.Content.ReadAsStringAsync() })
        {
            body.ShouldNotContain(webhook.Url);
            body.ShouldNotContain(token);
        }
    }

    [Fact]
    public async Task Discord_refusing_the_webhook_is_a_400_that_says_so_and_nothing_is_saved()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.UnknownWebhook();

        var response = await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: true, PostDigest: false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail!.ShouldContain("Discord did not accept");
        (await Get(hogs, hogs.Jacob)).Connected.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_flags_change_when_no_new_webhook_is_given_and_nothing_more_is_posted()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.NewWebhook();
        await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: false, PostDigest: false));

        var response = await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(null, AnnouncePayments: true, PostDigest: true));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<DiscordSettings>()).ShouldBe(new DiscordSettings(true, webhook.Tail, true, true));
        webhook.Posted.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Flags_cannot_be_set_before_a_webhook_is_connected()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(null, AnnouncePayments: true, PostDigest: false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail!.ShouldContain("webhook");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("http://discord.invalid/api/webhooks/100000000000000001/fake-token")]
    [InlineData("https://example.invalid/api/webhooks/100000000000000001/fake-token")]
    [InlineData("https://discord.invalid/somewhere/else")]
    [InlineData("https://user:pass@discord.invalid/api/webhooks/100000000000000001/fake-token")]
    public async Task Only_a_Discord_webhook_can_be_connected(string url)
    {
        var hogs = await HollandHogsSeason.Open(Api);

        var response = await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(url, AnnouncePayments: true, PostDigest: false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get(hogs, hogs.Jacob)).Connected.ShouldBeFalse();
    }

    [Fact]
    public async Task Disconnecting_forgets_the_webhook_and_the_flags()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.NewWebhook();
        await Put(hogs, hogs.Jacob, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: true, PostDigest: true));

        var response = await Api.CreateClientFor(hogs.Jacob).DeleteAsync(SettingsPath(hogs));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Get(hogs, hogs.Jacob)).ShouldBe(new DiscordSettings(false, null, false, false));
    }

    [Fact]
    public async Task Disconnecting_when_nothing_is_connected_is_still_a_success()
    {
        var hogs = await HollandHogsSeason.Open(Api);

        (await Api.CreateClientFor(hogs.Jacob).DeleteAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_member_cannot_read_change_or_disconnect_the_settings()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var webhook = Api.Discord.NewWebhook();
        var sam = Api.CreateClientFor(hogs.Sam);

        (await sam.GetAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Put(hogs, hogs.Sam, new DiscordSettingsRequest(webhook.Url, AnnouncePayments: true, PostDigest: false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sam.DeleteAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        webhook.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_treasurer_of_another_league_cannot_touch_the_settings()
    {
        var hogs = await HollandHogsSeason.Open(Api);
        var others = await HollandHogsSeason.Open(Api);

        (await Api.CreateClientFor(others.Jacob).GetAsync(SettingsPath(hogs))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static string SettingsPath(HollandHogsSeason hogs) => $"/leagues/{hogs.LeagueId}/notifications/discord";

    private Task<HttpResponseMessage> Put(HollandHogsSeason hogs, string subject, DiscordSettingsRequest body) =>
        Api.CreateClientFor(subject).PutAsJsonAsync(SettingsPath(hogs), body);

    private async Task<DiscordSettings> Get(HollandHogsSeason hogs, string subject) =>
        (await Api.CreateClientFor(subject).GetFromJsonAsync<DiscordSettings>(SettingsPath(hogs))).ShouldNotBeNull();
}
