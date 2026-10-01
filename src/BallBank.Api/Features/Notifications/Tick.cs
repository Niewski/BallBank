using BallBank.Api.Features.Treasury;
using BallBank.Domain.Membership;
using BallBank.Domain.Notifications;
using Marten;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BallBank.Api.Features.Notifications;

/// <summary>What one tick did for one league, as its summary line says.</summary>
/// <param name="Failed">Left pending because the channel refused: the next tick tries again.</param>
public sealed record TickSummary(Guid LeagueId, string Season, int Sent, int Held, int Skipped, int Failed);

/// <param name="LeaguesFailed">Leagues the tick could not finish; the others were not held up by them.</param>
public sealed record TickReport(IReadOnlyList<TickSummary> Leagues, int LeaguesFailed)
{
    /// <summary>Whether everything due was done: no league failed and no send was refused. A refused send is tried again by the next tick.</summary>
    public bool Succeeded => LeaguesFailed == 0 && Leagues.All(league => league.Failed == 0);
}

/// <summary>
/// The scheduled work (ADR-0007), done as of the injected clock and then over: for each league with an open season, the
/// reminder each member who still owes is due, and the held texts whose quiet hours have ended. Run by the Job's
/// <c>tick</c> command, hourly. It delivers as it goes rather than leaving it to the notifications queue, which a
/// process that exits cannot keep draining; a send the channel refuses stays pending and is tried again by the next tick.
/// A reminder is keyed on its account, due date and stage (<see cref="Reminders.Key"/>), so running twice sends once.
/// </summary>
public sealed class Tick(
    IDocumentStore store,
    NotificationChannels channels,
    IOptions<NotificationOptions> options,
    TimeProvider clock,
    ILogger<Tick> logger)
{
    // "BALLTICK": the Postgres advisory lock that lets one tick run at a time.
    private const long LockKey = 0x42414C4C5449434B;

    /// <summary>
    /// Does the tick's work for every league, or for <paramref name="onlyLeague"/> alone. A tick that finds another one
    /// running does nothing and answers an empty report: two at once could each find a reminder pending and send it.
    /// </summary>
    public async Task<TickReport> RunAsync(Guid? onlyLeague = null, CancellationToken cancellation = default)
    {
        await using var connection = store.Storage.Database.CreateConnection();
        await connection.OpenAsync(cancellation);
        if (!await TryLockAsync(connection, cancellation))
        {
            logger.LogInformation("Another tick is running; this one has nothing to do");
            return new TickReport([], 0);
        }

        try
        {
            return await TickLeaguesAsync(onlyLeague, cancellation);
        }
        finally
        {
            await UnlockAsync(connection);
        }
    }

    private async Task<TickReport> TickLeaguesAsync(Guid? onlyLeague, CancellationToken cancellation)
    {
        var summaries = new List<TickSummary>();
        var failed = 0;
        foreach (var season in await OpenSeasonsAsync(onlyLeague, cancellation))
        {
            using var scope = logger.BeginScope(new KeyValuePair<string, object>[] { new(TenantTelemetry.TenantId, season.LeagueId.ToString()) });
            try
            {
                var summary = await TickLeagueAsync(season, cancellation);
                logger.LogInformation(
                    "Tick for league {LeagueId}, season {Season}: sent {Sent}, held {Held}, skipped {Skipped}, failed {Failed}",
                    summary.LeagueId, summary.Season, summary.Sent, summary.Held, summary.Skipped, summary.Failed);
                summaries.Add(summary);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                failed++;
                logger.LogError(failure, "Tick for league {LeagueId}, season {Season} did not finish", season.LeagueId, season.Label);
            }
        }

        return new TickReport(summaries, failed);
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", LockKey);
        return (bool)(await command.ExecuteScalarAsync(cancellation))!;
    }

    private static async Task UnlockAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection);
        command.Parameters.AddWithValue("key", LockKey);
        await command.ExecuteScalarAsync();
    }

    // A league's open season is the one it opened last; seasons are not closed yet.
    private async Task<List<SeasonListing>> OpenSeasonsAsync(Guid? onlyLeague, CancellationToken cancellation)
    {
        await using var session = store.QuerySession();
        var listings = await session.Query<SeasonListing>().Where(season => season.AnyTenant()).ToListAsync(cancellation);
        return [.. listings
            .Where(season => onlyLeague is null || season.LeagueId == onlyLeague)
            .GroupBy(season => season.LeagueId)
            .Select(league => league.OrderByDescending(season => season.OpenedAt).First())];
    }

    private async Task<TickSummary> TickLeagueAsync(SeasonListing season, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var leagueId = season.LeagueId;

        await using var session = store.LightweightSession(leagueId.ToString());
        var league = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation)
            ?? throw new InvalidOperationException($"League {leagueId} has a season and no league stream.");
        var tally = new Tally();

        var statements = await session.Query<MemberStatement>().Where(statement => statement.Season == season.Label).ToListAsync(cancellation);
        var owing = statements.ToDictionary(statement => statement.Id, Owing);

        // What was decided before this tick goes first: texts held until now, and reminders a channel refused. What
        // this tick holds is held until later. A reminder goes with what the member owes now, or not at all.
        var undelivered = await session.Query<Notification>()
            .Where(notification =>
                (notification.Status == NotificationStatus.Held && notification.SendAfter <= now)
                || (notification.Kind == NotificationKinds.Reminder && notification.Status == NotificationStatus.Pending))
            .ToListAsync(cancellation);
        foreach (var notification in undelivered)
        {
            if (notification.Kind == NotificationKinds.Reminder && notification.AccountId is { } accountId)
            {
                var (balance, due) = owing.GetValueOrDefault(accountId);
                if (balance <= 0 || due is null)
                {
                    notification.Status = NotificationStatus.Skipped;
                    notification.Reason = Reminders.PaidUp;
                    session.Store(notification);
                    await session.SaveChangesAsync(cancellation);
                    tally.Skipped++;
                    continue;
                }

                notification.Text = ReminderText(league.Name, leagueId, accountId, balance, due.Value, today);
                session.Store(notification);
                await session.SaveChangesAsync(cancellation);
            }

            await DeliverAsync(session, leagueId, notification.Id, notification.Kind, tally, cancellation);
        }

        foreach (var statement in statements)
        {
            var (balance, due) = owing[statement.Id];
            if (Reminders.StageOn(balance, due, today) is not { } stage
                || league.Members.FirstOrDefault(member => member.MemberId == statement.MemberId) is not { } member)
            {
                continue;
            }

            var key = Reminders.Key(Channels.Sms, member.MemberId, statement.Id, due!.Value, stage);
            if (await session.LoadAsync<Notification>(key, cancellation) is not null)
            {
                continue;
            }

            // Claiming the key, before sending, is what makes a later tick send nothing.
            session.Insert(new Notification
            {
                Id = key,
                Kind = NotificationKinds.Reminder,
                Channel = Channels.Sms,
                MemberId = member.MemberId,
                AccountId = statement.Id,
                Text = ReminderText(league.Name, leagueId, statement.Id, balance, due.Value, today),
                CreatedAt = now,
            });
            await session.SaveChangesAsync(cancellation);

            await DeliverAsync(session, leagueId, key, NotificationKinds.Reminder, tally, cancellation);
        }

        return new TickSummary(leagueId, season.Label, tally.Sent, tally.Held, tally.Skipped, tally.Failed);
    }

    private static (decimal Balance, DateOnly? Due) Owing(MemberStatement statement) =>
        (statement.Totals().Balance,
            statement.Lines
                .Where(line => line is { Kind: StatementLineKind.Assessment, DueDate: not null })
                .Select(line => line.DueDate)
                .Min());

    private string ReminderText(string leagueName, Guid leagueId, Guid accountId, decimal balance, DateOnly due, DateOnly today) =>
        SmsTexts.Reminder(leagueName, balance, due, today, SmsTexts.StatementLink(options.Value.WebBaseUrl, leagueId, accountId));

    private async Task DeliverAsync(IDocumentSession session, Guid leagueId, string notificationId, string kind, Tally tally, CancellationToken cancellation)
    {
        try
        {
            await SendNotificationHandler.Handle(new SendNotification(leagueId, notificationId), session, store, channels, clock, cancellation);
            await session.SaveChangesAsync(cancellation);
        }
        catch (NotificationDeliveryException failure)
        {
            logger.LogWarning(failure, "A {Kind} text for league {LeagueId} was not accepted and stays pending", kind, leagueId);
            tally.Failed++;
            return;
        }

        switch ((await session.LoadAsync<Notification>(notificationId, cancellation))?.Status)
        {
            case NotificationStatus.Sent:
                tally.Sent++;
                break;
            case NotificationStatus.Held:
                tally.Held++;
                break;
            case NotificationStatus.Skipped:
                tally.Skipped++;
                break;
        }
    }

    private sealed class Tally
    {
        public int Sent;
        public int Held;
        public int Skipped;
        public int Failed;
    }
}
