using System.Text.RegularExpressions;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wolverine.Http;

namespace BallBank.Api.Integrations.Twilio;

/// <summary>What Twilio tells BallBank, outside any league's route: a request is Twilio's only if its <see cref="TwilioSignature"/> is valid, else 403.</summary>
public static partial class TwilioWebhooksEndpoint
{
    public const string InboundPath = "/webhooks/twilio/inbound";

    /// <summary>How a text ended up; the league and notification come from the signed address, never the body (ADR-0005).</summary>
    [AllowAnonymous]
    [WolverinePost(TwilioSmsChannel.StatusCallbackPath)]
    public static async Task<IResult> Status(
        HttpContext http,
        IOptions<TwilioOptions> options,
        IDocumentStore store,
        ILogger<TwilioSmsChannel> logger,
        CancellationToken cancellation)
    {
        var form = await SignedFormAsync(http, options.Value, logger, cancellation);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var notificationId = http.Request.Query["notification"].ToString();
        if (!Guid.TryParse(http.Request.Query["league"].ToString(), out var leagueId) || notificationId.Length == 0)
        {
            return Results.NotFound();
        }

        // The tenant is the league named in the address; this endpoint is outside every league's route.
        await using var session = store.LightweightSession(leagueId.ToString());
        var notification = await session.LoadAsync<Notification>(notificationId, cancellation);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (TextDeliveries.Read(form.GetValueOrDefault("MessageStatus")) is { } delivery
            && notification.Settle(delivery, form.GetValueOrDefault("ErrorCode")))
        {
            session.Store(notification);
            await session.SaveChangesAsync(cancellation);
        }

        return Results.Ok();
    }

    /// <summary>A member's reply: STOP opts the number out in every league, START takes that back, anything else is ignored.</summary>
    [AllowAnonymous]
    [WolverinePost(InboundPath)]
    public static async Task<IResult> Inbound(
        HttpContext http,
        IOptions<TwilioOptions> options,
        IDocumentSession session,
        TimeProvider clock,
        ILogger<TwilioSmsChannel> logger,
        CancellationToken cancellation)
    {
        var form = await SignedFormAsync(http, options.Value, logger, cancellation);
        if (form is null)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var from = form.GetValueOrDefault("From")?.Trim();
        if (from is null || !E164().IsMatch(from))
        {
            return Results.Ok();
        }

        switch (SmsReplies.Classify(form.GetValueOrDefault("Body")))
        {
            case SmsReply.Stop:
                // A second STOP keeps the first's time: the number opted out when it first said so.
                if (await session.LoadAsync<PhoneOptOut>(from, cancellation) is null)
                {
                    session.Store(new PhoneOptOut { Id = from, OptedOutAt = clock.GetUtcNow() });
                }

                break;
            case SmsReply.Start:
                session.Delete<PhoneOptOut>(from);
                break;
        }

        return Results.Ok();
    }

    /// <summary>The posted form, or <c>null</c> when the request is not Twilio's.</summary>
    private static async Task<Dictionary<string, string>?> SignedFormAsync(
        HttpContext http, TwilioOptions settings, ILogger logger, CancellationToken cancellation)
    {
        var request = http.Request;
        if (!settings.IsConfigured || !request.HasFormContentType)
        {
            return null;
        }

        var posted = await request.ReadFormAsync(cancellation);
        var pairs = posted.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value ?? string.Empty))).ToList();

        // The address as Twilio was given it: the public base, not whatever host a proxy forwarded the request as.
        var url = $"{settings.StatusCallbackBaseUrl!.TrimEnd('/')}{request.Path}{request.QueryString}";
        if (!TwilioSignature.IsValid(url, pairs, settings.AuthToken, request.Headers[TwilioSignature.Header]))
        {
            logger.LogWarning("A request to {Path} was refused: its Twilio signature did not match.", request.Path);
            return null;
        }

        return pairs.GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.First().Value);
    }

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();
}
