using System.Globalization;
using System.Security.Claims;
using BallBank.Api.Features.Membership;
using BallBank.Domain.Membership;
using BallBank.Domain.Treasury;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>One event of an account's stream, as <c>GET /leagues/{leagueId}/accounts/{accountId}/history</c> serves it.</summary>
/// <param name="Sequence">The event's place among every event of the store.</param>
/// <param name="Version">The account's stream version once this event was recorded.</param>
/// <param name="At">When it happened, as the event itself says.</param>
/// <param name="Type">The event type, e.g. <c>PaymentConfirmed</c>.</param>
/// <param name="By">The acting member.</param>
/// <param name="ByName">What the acting member goes by; <c>null</c> for a member the league no longer lists.</param>
/// <param name="Sentence">What happened, in league language.</param>
public sealed record AccountHistoryEntry(
    long Sequence,
    long Version,
    DateTimeOffset At,
    string Type,
    Guid By,
    string? ByName,
    string Sentence);

public static class AccountHistoryEndpoint
{
    /// <summary>
    /// Every event of one account, in order, with who and when, for a treasurer of the league or the
    /// account's own member. Read straight from the stream: the history is the audit trail, nothing is stored.
    /// </summary>
    [Authorize(Policy = Policies.LeagueMember)]
    [WolverineGet("/leagues/{leagueId}/accounts/{accountId}/history")]
    public static async Task<IResult> Get(
        Guid leagueId,
        Guid accountId,
        ClaimsPrincipal user,
        IQuerySession session,
        IDocumentStore store,
        CancellationToken cancellation)
    {
        var events = await session.Events.FetchStreamAsync(accountId, token: cancellation);
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (events.FirstOrDefault()?.Data is not AccountOpened opened || league is null)
        {
            return Results.NotFound();
        }

        if (await AccountReaders.CallerAsync(store, user, leagueId, opened.MemberId, cancellation) is null)
        {
            return AccountReaders.NotYourAccount("history");
        }

        var members = league.Members.ToDictionary(m => m.MemberId);
        return Results.Ok(AccountHistory.Of(events, members));
    }
}

/// <summary>Renders an account's stream as sentences a member of the league reads.</summary>
public static class AccountHistory
{
    public static AccountHistoryEntry[] Of(IReadOnlyList<IEvent> events, IReadOnlyDictionary<Guid, Member> members)
    {
        // A confirmation or rejection names only its attestation; the sentence names the payment.
        var attestations = new Dictionary<Guid, PaymentAttested>();

        // AccountOpened names no one: an account is only ever opened to assess dues, in the same step,
        // so whoever assessed its first dues opened it.
        var openedBy = events.Select(e => e.Data).OfType<DuesAssessed>().FirstOrDefault()?.AssessedBy ?? Guid.Empty;

        return events
            .Select(e =>
            {
                var (by, at, sentence) = e.Data switch
                {
                    AccountOpened opened => (openedBy, opened.OpenedAt,
                        $"{Name(openedBy)} opened {Name(opened.MemberId)}'s account for the {opened.Season} season"),
                    DuesAssessed assessed => (assessed.AssessedBy, assessed.AssessedAt,
                        $"{Name(assessed.AssessedBy)} assessed {Money(assessed.Amount)} for {assessed.Memo}, due {assessed.DueDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"),
                    PaymentAttested attested => (attested.AttestedBy, attested.AttestedAt,
                        $"{Name(attested.AttestedBy)} attested a {Payment(attested)}"),
                    PaymentConfirmed confirmed => (confirmed.ConfirmedBy, confirmed.ConfirmedAt,
                        $"{Name(confirmed.ConfirmedBy)} confirmed the {Payment(attestations[confirmed.AttestationId])}"),
                    PaymentRejected rejected => (rejected.RejectedBy, rejected.RejectedAt,
                        $"{Name(rejected.RejectedBy)} rejected the {Payment(attestations[rejected.AttestationId])}: {rejected.Reason}"),
                    AdjustmentPosted { Refund: true } refund => (refund.PostedBy, refund.PostedAt,
                        $"{Name(refund.PostedBy)} refunded {Money(refund.Amount)} out of the pot: {refund.Reason}"),
                    AdjustmentPosted adjusted => (adjusted.PostedBy, adjusted.PostedAt,
                        $"{Name(adjusted.PostedBy)} {(adjusted.Amount < 0 ? "lowered" : "raised")} the balance by {Money(adjusted.Amount)}: {adjusted.Reason}"),
                    var unknown => throw new InvalidOperationException($"No sentence for the event {unknown.GetType().Name}."),
                };

                if (e.Data is PaymentAttested recorded)
                {
                    attestations[recorded.AttestationId] = recorded;
                }

                return new AccountHistoryEntry(e.Sequence, e.Version, at, e.Data.GetType().Name, by, MemberNames.ActingName(members, by), sentence);
            })
            .ToArray();

        string Name(Guid memberId) => MemberNames.ActingName(members, memberId) ?? "A former member";
    }

    private static string Payment(PaymentAttested attested) =>
        $"{Money(attested.Amount)} {attested.Rail} payment{(attested.Reference is { } reference ? $" {reference}" : "")}";

    private static string Money(decimal amount) => $"${Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture)}";
}
