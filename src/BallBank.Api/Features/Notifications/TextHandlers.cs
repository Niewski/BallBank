using BallBank.Api.Features.Treasury;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Runtime.Handlers;

namespace BallBank.Api.Features.Notifications;

/// <summary>Records a text for each member in <paramref name="memberIds"/>; whether they are texted is decided when it goes (ADR-0008).</summary>
internal static class Texting
{
    public static OutgoingMessages Tell(
        IDocumentSession session, TimeProvider clock, Guid leagueId, string kind, Guid cause, IEnumerable<Guid> memberIds, string text)
    {
        var messages = new OutgoingMessages();
        foreach (var memberId in memberIds)
        {
            var notification = new Notification
            {
                Id = NotificationKey.For(kind, cause, Channels.Sms, memberId),
                Kind = kind,
                Channel = Channels.Sms,
                MemberId = memberId,
                Text = text,
                CreatedAt = clock.GetUtcNow(),
            };
            session.Insert(notification);
            messages.Add(new SendNotification(leagueId, notification.Id));
        }

        return messages;
    }
}

/// <summary>An account, the league it is in and the member who holds it, as the handlers of its events need them.</summary>
internal sealed record TextedAccount(MemberStatement Statement, League League, Member Member)
{
    public Guid LeagueId => Statement.LeagueId;

    public string Link(IOptions<NotificationOptions> options) =>
        SmsTexts.StatementLink(options.Value.WebBaseUrl, LeagueId, Statement.Id);

    /// <summary>The attestation as the statement tells it, whatever has become of it since.</summary>
    public StatementLine? Payment(Guid attestationId) =>
        Statement.Lines.FirstOrDefault(line => line.Kind == StatementLineKind.Attestation && line.Id == attestationId);

    public static async Task<TextedAccount?> LoadAsync(IDocumentSession session, Guid accountId, CancellationToken cancellation)
    {
        // The account's statement is built in the transaction that committed the event, so it is current.
        if (await session.LoadAsync<MemberStatement>(accountId, cancellation) is not { } statement
            || await session.Events.AggregateStreamAsync<League>(statement.LeagueId, token: cancellation) is not { } league
            || league.Members.FirstOrDefault(listed => listed.MemberId == statement.MemberId) is not { } member)
        {
            return null;
        }

        return new TextedAccount(statement, league, member);
    }
}

/// <summary>Dues were assessed to a member: they hear the amount, what it is for and when it is due.</summary>
public static class DuesAssessedTextHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<OutgoingMessages> Handle(
        IEvent<DuesAssessed> assessed,
        IDocumentSession session,
        IOptions<NotificationOptions> options,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        if (await TextedAccount.LoadAsync(session, assessed.StreamId, cancellation) is not { } account)
        {
            return [];
        }

        var dues = assessed.Data;
        return Texting.Tell(
            session, clock, account.LeagueId, NotificationKinds.DuesAssessed, dues.AssessmentId, [account.Member.MemberId],
            SmsTexts.DuesAssessed(account.League.Name, dues.Amount, dues.Memo, dues.DueDate, account.Link(options)));
    }
}

/// <summary>A member attested a payment: every treasurer but the one who attested hears who, how much, and how it was paid.</summary>
public static class PaymentAttestedTextHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<OutgoingMessages> Handle(
        IEvent<PaymentAttested> attested,
        IDocumentSession session,
        IOptions<NotificationOptions> options,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        if (await TextedAccount.LoadAsync(session, attested.StreamId, cancellation) is not { } account)
        {
            return [];
        }

        var payment = attested.Data;
        var members = account.League.Members.ToDictionary(member => member.MemberId);
        var treasurers = account.League.Members
            .Where(member => member.IsTreasurer && member.MemberId != payment.AttestedBy)
            .Select(member => member.MemberId);

        return Texting.Tell(
            session, clock, account.LeagueId, NotificationKinds.PaymentAttested, payment.AttestationId, treasurers,
            SmsTexts.PaymentAttested(
                account.League.Name,
                MemberNames.ActingName(members, account.Member.MemberId) ?? account.Member.TeamName,
                payment.Amount,
                payment.Rail,
                payment.Reference,
                account.Link(options)));
    }
}

/// <summary>A treasurer confirmed a payment: the member hears it, unless they confirmed it themselves.</summary>
public static class PaymentConfirmedTextHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<OutgoingMessages> Handle(
        IEvent<PaymentConfirmed> confirmed,
        IDocumentSession session,
        IOptions<NotificationOptions> options,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        if (await TextedAccount.LoadAsync(session, confirmed.StreamId, cancellation) is not { } account
            || account.Member.MemberId == confirmed.Data.ConfirmedBy
            || account.Payment(confirmed.Data.AttestationId) is not { Rail: { } rail } payment)
        {
            return [];
        }

        return Texting.Tell(
            session, clock, account.LeagueId, NotificationKinds.PaymentConfirmed, confirmed.Data.AttestationId, [account.Member.MemberId],
            SmsTexts.PaymentConfirmed(account.League.Name, payment.Amount, rail, account.Link(options)));
    }
}

/// <summary>A treasurer rejected a payment: the member hears why, unless they rejected it themselves.</summary>
public static class PaymentRejectedTextHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<OutgoingMessages> Handle(
        IEvent<PaymentRejected> rejected,
        IDocumentSession session,
        IOptions<NotificationOptions> options,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        if (await TextedAccount.LoadAsync(session, rejected.StreamId, cancellation) is not { } account
            || account.Member.MemberId == rejected.Data.RejectedBy
            || account.Payment(rejected.Data.AttestationId) is not { Rail: { } rail } payment)
        {
            return [];
        }

        return Texting.Tell(
            session, clock, account.LeagueId, NotificationKinds.PaymentRejected, rejected.Data.AttestationId, [account.Member.MemberId],
            SmsTexts.PaymentRejected(account.League.Name, payment.Amount, rail, rejected.Data.Reason, account.Link(options)));
    }
}

/// <summary>A treasurer adjusted a member's balance: the member hears why, unless they posted it themselves.</summary>
public static class AdjustmentPostedTextHandler
{
    public static void Configure(HandlerChain chain) => Announcing.DiscardDuplicates(chain);

    public static async Task<OutgoingMessages> Handle(
        IEvent<AdjustmentPosted> posted,
        IDocumentSession session,
        IOptions<NotificationOptions> options,
        TimeProvider clock,
        CancellationToken cancellation)
    {
        if (await TextedAccount.LoadAsync(session, posted.StreamId, cancellation) is not { } account
            || account.Member.MemberId == posted.Data.PostedBy)
        {
            return [];
        }

        var adjustment = posted.Data;
        return Texting.Tell(
            session, clock, account.LeagueId, NotificationKinds.AdjustmentPosted, adjustment.AdjustmentId, [account.Member.MemberId],
            SmsTexts.AdjustmentPosted(account.League.Name, adjustment.Amount, adjustment.Reason, adjustment.Refund, account.Link(options)));
    }
}
