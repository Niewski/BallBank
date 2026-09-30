using BallBank.Api.Features.Treasury;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// Who is told what (ADR-0008): each handler here hears one Treasury event, which Marten forwards to Wolverine
/// in the transaction that commits it, and decides what the league's channel is told, if anything. What it
/// decides is recorded as a <see cref="Notification"/> in the transaction that handles the event, together with
/// the <see cref="SendNotification"/> that delivers it. The notification's id is its dedupe key, so an event
/// handled a second time finds the key taken, its transaction is rolled back and nothing more is sent.
/// </summary>
internal static class Announcing
{
    public static SendNotification Tell(IDocumentSession session, TimeProvider clock, Guid leagueId, string kind, Guid cause, string text)
    {
        var notification = new Notification
        {
            Id = NotificationKey.For(kind, cause, Channels.Discord, leagueId),
            Kind = kind,
            Channel = Channels.Discord,
            Text = text,
            CreatedAt = clock.GetUtcNow(),
        };
        session.Insert(notification);

        return new SendNotification(leagueId, notification.Id);
    }

    // A duplicate is the point of the key, not a failure: the first handling already told the league.
    public static void DiscardDuplicates(HandlerChain chain) => chain.OnException<DocumentAlreadyExistsException>().Discard();
}

/// <summary>A season opened: the league's channel hears its dues and due date.</summary>
public static class SeasonOpenedAnnouncementHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<SendNotification?> Handle(
        IEvent<SeasonOpened> opened,
        IDocumentSession session,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        var season = opened.Data;
        if (await session.LoadAsync<LeagueNotificationSettings>(season.LeagueId, cancellation) is null
            || await session.Events.AggregateStreamAsync<League>(season.LeagueId, token: cancellation) is not { } league)
        {
            return null;
        }

        return Announcing.Tell(
            session, clock, season.LeagueId, NotificationKinds.SeasonOpened, season.SeasonId,
            NotificationTexts.SeasonOpened(league.Name, season.Label, season.DuesAmount, season.DueDate));
    }
}

/// <summary>
/// A payment was confirmed: the league's channel hears whose it was, how much, and what the pot holds now,
/// if the league chose to announce payments. Attesting and rejecting are not announced.
/// </summary>
public static class PaymentConfirmedAnnouncementHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<SendNotification?> Handle(
        IEvent<PaymentConfirmed> confirmed,
        IDocumentSession session,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        // The account's statement is built in the transaction that committed the event, so it is current.
        if (await session.LoadAsync<MemberStatement>(confirmed.StreamId, cancellation) is not { } statement
            || await session.LoadAsync<LeagueNotificationSettings>(statement.LeagueId, cancellation) is not { AnnouncePayments: true }
            || statement.Lines.FirstOrDefault(line => line.Kind == StatementLineKind.Attestation && line.Id == confirmed.Data.AttestationId) is not { } payment
            || await session.Events.AggregateStreamAsync<League>(statement.LeagueId, token: cancellation) is not { } league
            || league.Members.FirstOrDefault(member => member.MemberId == statement.MemberId) is not { } payer)
        {
            return null;
        }

        var accounts = await session.Query<MemberStatement>()
            .Where(other => other.LeagueId == statement.LeagueId && other.Season == statement.Season)
            .ToListAsync(cancellation);

        return Announcing.Tell(
            session, clock, statement.LeagueId, NotificationKinds.PaymentConfirmed, confirmed.Data.AttestationId,
            NotificationTexts.PaymentConfirmed(payer.TeamName, payment.Amount, pot: accounts.Sum(account => account.InThePot())));
    }
}
