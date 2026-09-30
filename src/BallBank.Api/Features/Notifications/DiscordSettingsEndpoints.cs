using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wolverine.Http;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// The body of <c>PUT /leagues/{leagueId}/notifications/discord</c>. Both flags are as entered: one left
/// out is off.
/// </summary>
/// <param name="WebhookUrl">The channel's webhook, copied from Discord. Left out when only the flags change.</param>
/// <param name="AnnouncePayments">Announce each confirmed payment in the channel.</param>
/// <param name="PostDigest">Post the weekly digest in the channel.</param>
public sealed record DiscordSettingsRequest(string? WebhookUrl, bool AnnouncePayments = false, bool PostDigest = false);

/// <summary>What is known of a league's Discord connection. The webhook itself is never part of it.</summary>
/// <param name="WebhookTail">The last few characters of the webhook, to recognise it by; <c>null</c> when not connected.</param>
public sealed record DiscordSettings(bool Connected, string? WebhookTail, bool AnnouncePayments, bool PostDigest)
{
    public static DiscordSettings Of(LeagueNotificationSettings? settings) =>
        settings is null
            ? new DiscordSettings(false, null, false, false)
            : new DiscordSettings(true, DiscordWebhook.Tail(settings.DiscordWebhookUrl), settings.AnnouncePayments, settings.PostDigest);
}

public static class DiscordSettingsEndpoints
{
    /// <summary>Whether Discord is connected, the tail of its webhook and the flags.</summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverineGet("/leagues/{leagueId}/notifications/discord")]
    public static async Task<DiscordSettings> Get(Guid leagueId, IQuerySession session, CancellationToken cancellation) =>
        DiscordSettings.Of(await session.LoadAsync<LeagueNotificationSettings>(leagueId, cancellation));

    /// <summary>
    /// Connects the league's Discord channel, or changes what is announced in it. A webhook that is given is
    /// tried first, with a hello posted through it in this request, and is kept only if Discord takes it.
    /// Written straight to <see cref="LeagueNotificationSettings"/>, with no event (ADR-0012).
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverinePut("/leagues/{leagueId}/notifications/discord")]
    public static async Task<IResult> Put(
        Guid leagueId,
        DiscordSettingsRequest request,
        ClaimsPrincipal user,
        IDocumentSession session,
        NotificationChannels channels,
        IOptions<NotificationOptions> options,
        CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null)
        {
            return Results.NotFound();
        }

        if (RefuseUnlessTreasurer(league, user) is { } refusal)
        {
            return refusal;
        }

        string webhookUrl;
        if (string.IsNullOrWhiteSpace(request.WebhookUrl))
        {
            var connected = await session.LoadAsync<LeagueNotificationSettings>(leagueId, cancellation);
            if (connected is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "No webhook",
                    detail: "Paste the Discord webhook URL to connect Discord before choosing what is announced.");
            }

            webhookUrl = connected.DiscordWebhookUrl;
        }
        else
        {
            try
            {
                webhookUrl = DiscordWebhook.Accept(request.WebhookUrl, options.Value.AllowedDiscordHosts);
            }
            catch (DomainException notAWebhook)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Not a Discord webhook",
                    detail: notAWebhook.Message);
            }

            try
            {
                await channels.For(Channels.Discord).SendAsync(webhookUrl, NotificationTexts.Hello(league.Name), cancellation);
            }
            catch (NotificationDeliveryException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Discord refused the webhook",
                    detail: "Discord did not accept that webhook. Check that it was copied whole, and that it has not been deleted in Discord.");
            }
        }

        var settings = new LeagueNotificationSettings
        {
            Id = leagueId,
            DiscordWebhookUrl = webhookUrl,
            AnnouncePayments = request.AnnouncePayments,
            PostDigest = request.PostDigest,
        };
        session.Store(settings);

        return Results.Ok(DiscordSettings.Of(settings));
    }

    /// <summary>
    /// Disconnects Discord: the webhook and the flags are forgotten, and nothing is posted afterwards,
    /// not even what was already waiting to be sent. Disconnecting when not connected changes nothing.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverineDelete("/leagues/{leagueId}/notifications/discord")]
    public static async Task<IResult> Delete(
        Guid leagueId,
        ClaimsPrincipal user,
        IDocumentSession session,
        CancellationToken cancellation)
    {
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (league is null)
        {
            return Results.NotFound();
        }

        if (RefuseUnlessTreasurer(league, user) is { } refusal)
        {
            return refusal;
        }

        session.Delete<LeagueNotificationSettings>(leagueId);
        return Results.NoContent();
    }

    // The policy asked the caller's memberships; the league, the source of truth, is asked again (ADR-0011).
    // Where the league's announcements go is not for someone whose claim was revoked since to change.
    private static IResult? RefuseUnlessTreasurer(League league, ClaimsPrincipal user) =>
        league.MemberHeldBy(user.Subject()) is { IsTreasurer: true }
            ? null
            : Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a treasurer",
                detail: $"Only a treasurer of {league.Name} can change how it is told things.");
}
