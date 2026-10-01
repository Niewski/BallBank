using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using BallBank.Domain.Treasury;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Treasury;

/// <summary>What a season's accounts add up to. <c>Owed</c> is what the pot owes members, as a positive amount.</summary>
public sealed record DashboardFigures(
    decimal Assessed,
    decimal Confirmed,
    decimal Refunded,
    decimal Adjusted,
    decimal Pot,
    decimal Outstanding,
    decimal Owed,
    int PendingAttestations);

/// <summary>A member who still owes after the earliest date anything was due.</summary>
/// <param name="DisplayName">What the member goes by; <c>null</c> when nothing is known.</param>
/// <param name="EarliestDueDate">The earliest date anything assessed to the account was due.</param>
/// <param name="LastReminder">The reminder the tick decided for the account last; <c>null</c> when it has not yet decided one.</param>
public sealed record Delinquent(
    Guid AccountId,
    Guid MemberId,
    string TeamName,
    string? DisplayName,
    decimal Balance,
    DateOnly EarliestDueDate,
    int DaysOverdue,
    LastReminder? LastReminder);

/// <summary>How the last reminder for an account went.</summary>
/// <param name="At">When it was sent; when it was decided, if it has not been.</param>
/// <param name="Status">One of <see cref="NotificationStatus"/>: sent (and, once Twilio has reported, delivered, undelivered or failed), held for the member's quiet hours, skipped for want of consent, or still pending.</param>
/// <param name="Reason">Why it was skipped or held, or the error Twilio gave when it did not deliver it.</param>
public sealed record LastReminder(DateTimeOffset At, string Status, string? Reason);

/// <summary>The treasurer's dashboard for one season.</summary>
/// <param name="AsOf">When the last event it reflects was recorded; <c>null</c> until the projection has built it.</param>
public sealed record Dashboard(string Season, DashboardFigures Figures, Delinquent[] Delinquents, DateTimeOffset? AsOf);

public static class DashboardEndpoint
{
    /// <summary>
    /// The season's figures and delinquents, read from <see cref="LeaguePot"/> (ADR-0006): built by the
    /// projection daemon, so it can trail a command by a moment, which <c>AsOf</c> says. Days overdue
    /// count from the UTC date of the API's clock.
    /// </summary>
    [Authorize(Policy = Policies.LeagueTreasurer)]
    [WolverineGet("/leagues/{leagueId}/seasons/{season}/dashboard")]
    public static async Task<IResult> Get(Guid leagueId, string season, IQuerySession session, TimeProvider clock, CancellationToken cancellation)
    {
        var seasonId = SeasonIds.SeasonId(leagueId, season);
        var listing = await session.LoadAsync<SeasonListing>(seasonId, cancellation);
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (listing is null || league is null)
        {
            return Results.NotFound();
        }

        var pot = await session.LoadAsync<LeaguePot>(seasonId, cancellation);
        if (pot is null)
        {
            return Results.Ok(new Dashboard(listing.Label, new DashboardFigures(0, 0, 0, 0, 0, 0, 0, 0), [], AsOf: null));
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var members = league.Members.ToDictionary(m => m.MemberId);

        var owing = pot.Accounts
            .Select(account => (Account: account, DaysOverdue: Delinquency.DaysOverdue(account.Balance, account.EarliestDueDate, today)))
            .Where(account => account.DaysOverdue is not null && members.ContainsKey(account.Account.MemberId))
            .ToList();
        var lastReminders = await LastRemindersAsync(session, [.. owing.Select(account => (Guid?)account.Account.AccountId)], cancellation);

        var delinquents = owing
            .Select(account =>
            {
                var member = members[account.Account.MemberId];
                return new Delinquent(
                    account.Account.AccountId,
                    member.MemberId,
                    member.TeamName,
                    MemberNames.DisplayName(member),
                    account.Account.Balance,
                    account.Account.EarliestDueDate!.Value,
                    account.DaysOverdue!.Value,
                    lastReminders.GetValueOrDefault(account.Account.AccountId));
            })
            .OrderByDescending(d => d.DaysOverdue)
            .ThenByDescending(d => d.Balance)
            .ThenBy(d => d.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new Dashboard(
            listing.Label,
            new DashboardFigures(pot.Assessed, pot.Confirmed, pot.Refunded, pot.Adjusted, pot.Pot, pot.Outstanding, pot.Owed, pot.PendingAttestations),
            delinquents,
            pot.AsOf));
    }

    // Read from the notifications themselves, not the pot: the tick records a reminder as a notification and nothing else.
    private static async Task<Dictionary<Guid, LastReminder>> LastRemindersAsync(IQuerySession session, Guid?[] accountIds, CancellationToken cancellation)
    {
        var reminders = await session.Query<Notification>()
            .Where(notification => notification.Kind == NotificationKinds.Reminder && notification.AccountId.IsOneOf(accountIds))
            .ToListAsync(cancellation);

        return reminders
            .GroupBy(reminder => reminder.AccountId!.Value)
            .ToDictionary(
                account => account.Key,
                account =>
                {
                    var last = account.OrderByDescending(reminder => reminder.CreatedAt).First();
                    return new LastReminder(last.SentAt ?? last.CreatedAt, last.Status, last.Reason);
                });
    }
}
