using System.Security.Claims;
using BallBank.Api.Integrations.Sleeper;
using BallBank.Domain.Membership;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace BallBank.Api.Features.Membership;

/// <summary>The body of <c>POST /leagues/{leagueId}/import</c>.</summary>
/// <param name="SleeperUsername">The importer's Sleeper username; only the first import reads it.</param>
/// <param name="DisplayName">What the importer wants to be called in this league; only the first import reads it.</param>
public sealed record ImportLeagueRequest(string SleeperLeagueId, string? SleeperUsername = null, string? DisplayName = null);

/// <summary>The league an import created or added to, as its importer holds it.</summary>
/// <param name="MembersAdded">How many members this import added: every team the first time, only new ones after.</param>
public sealed record ImportedLeague(
    Guid LeagueId,
    string Name,
    string Season,
    int Members,
    int MembersAdded,
    Guid MemberId,
    IReadOnlyList<string> Roles);

public static class ImportLeagueEndpoint
{
    /// <summary>
    /// Brings a Sleeper league into BallBank as <paramref name="leagueId"/>, which the client chooses,
    /// or, when that league exists, imports it again to add the teams that joined since.
    /// Everything Sleeper is asked happens before anything is written, and everything written commits
    /// in one transaction, so a failure part-way leaves nothing behind.
    /// </summary>
    [Authorize]
    [WolverinePost("/leagues/{leagueId}/import")]
    public static async Task<IResult> Post(
        Guid leagueId,
        ImportLeagueRequest request,
        ClaimsPrincipal user,
        IDocumentStore store,
        SleeperClient sleeper,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(request.SleeperLeagueId))
        {
            return Incomplete("Importing a league needs the Sleeper league.");
        }

        // The session is opened on the canonical form of the league id, so the tenant never depends on
        // how the client spelled the GUID. It is committed explicitly below, which is why this endpoint
        // takes the store rather than a session Wolverine would commit for it.
        await using var session = store.LightweightSession(leagueId.ToString());

        var stream = await session.Events.FetchForWriting<League>(leagueId, cancellation);
        return stream.Aggregate is null
            ? await ImportFirst(leagueId, request, user.Subject(), session, sleeper, cancellation)
            : await ImportAgain(stream, request.SleeperLeagueId.Trim(), user.Subject(), session, sleeper, cancellation);
    }

    /// <summary>
    /// The first import writes the league stream, the raw Sleeper responses, the Sleeper league index
    /// and the importer's membership.
    /// </summary>
    private static async Task<IResult> ImportFirst(
        Guid leagueId,
        ImportLeagueRequest request,
        string subject,
        IDocumentSession session,
        SleeperClient sleeper,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(request.SleeperUsername) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Incomplete("Importing a league needs the Sleeper league, your Sleeper username and the name you go by.");
        }

        var sleeperUser = await sleeper.FindUserAsync(request.SleeperUsername.Trim(), cancellation);
        if (sleeperUser is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No such Sleeper user",
                detail: $"Sleeper has no user called \"{request.SleeperUsername.Trim()}\". Check the spelling of your Sleeper username.");
        }

        var responses = await sleeper.GetLeagueResponsesAsync(request.SleeperLeagueId.Trim(), cancellation);
        if (responses is null)
        {
            return NoSuchSleeperLeague();
        }

        var snapshot = responses.ToSnapshot();

        // Only someone who owns a team in the Sleeper league may bring it in. The domain refuses this
        // too; asking here first makes it a 403 rather than a 409.
        if (snapshot.RosterOwnedBy(sleeperUser.UserId) is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not your league",
                detail: $"{sleeperUser.Username} does not own a team in {snapshot.Name} on Sleeper, so cannot import it.");
        }

        var backing = await session.LoadAsync<SleeperLeagueIndex>(snapshot.SleeperLeagueId, cancellation);
        var now = DateTimeOffset.UtcNow;
        var snapshotId = Guid.NewGuid();

        var events = League.Import(
            new ImportLeague(
                leagueId,
                snapshotId,
                snapshot,
                snapshot.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
                subject,
                sleeperUser.UserId,
                request.DisplayName,
                backing?.LeagueId),
            now);

        var league = League.Replay((LeagueImported)events[0], events.Skip(1));
        var importersMember = league.MemberHeldBy(subject)
            ?? throw new InvalidOperationException("An import left the importer holding no member.");
        var summary = Summary(league, importersMember, events);

        session.Events.StartStream<League>(leagueId, events);
        session.Store(SleeperSnapshot.Of(snapshotId, responses, now));
        session.Insert(new SleeperLeagueIndex { Id = snapshot.SleeperLeagueId, LeagueId = leagueId });

        var memberships = await session.LoadAsync<UserMemberships>(subject, cancellation)
            ?? new UserMemberships { Id = subject };
        memberships.Leagues.Add(new LeagueMembership(leagueId, league.Name, league.Season, importersMember.MemberId, summary.Roles));
        session.Store(memberships);

        try
        {
            await session.SaveChangesAsync(cancellation);
        }
        catch (Exception raced) when (raced is ExistingStreamIdCollisionException or DocumentAlreadyExistsException)
        {
            // Someone imported this league, or this Sleeper league, between the checks above and now.
            return AlreadyImported();
        }

        return Results.Created((string?)null, summary);
    }

    /// <summary>
    /// Importing again, by a treasurer, adds a member for each team that joined on Sleeper since and
    /// keeps what Sleeper said. The league already backs this Sleeper league, so the index is left as
    /// it is, and nobody's membership changes: the members added are unclaimed. A retry after a lost
    /// response comes here too, and adds nobody.
    /// </summary>
    private static async Task<IResult> ImportAgain(
        JasperFx.Events.IEventStream<League> stream,
        string sleeperLeagueId,
        string subject,
        IDocumentSession session,
        SleeperClient sleeper,
        CancellationToken cancellation)
    {
        var league = stream.Aggregate!;

        // Someone who is not a member learns no more than they would importing a league that exists.
        if (league.MemberHeldBy(subject) is not { } held)
        {
            return AlreadyImported();
        }

        // The domain refuses both of these too; asking here first gives each its own status, and
        // spares Sleeper a call.
        if (!held.IsTreasurer)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a treasurer",
                detail: $"Only a treasurer of {league.Name} can import it again.");
        }

        if (league.SleeperLeagueId != sleeperLeagueId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A different Sleeper league",
                detail: $"{league.Name} is kept from a different Sleeper league. Import that one as a league of its own.");
        }

        var responses = await sleeper.GetLeagueResponsesAsync(sleeperLeagueId, cancellation);
        if (responses is null)
        {
            return NoSuchSleeperLeague();
        }

        var snapshot = responses.ToSnapshot();
        var now = DateTimeOffset.UtcNow;

        var events = league.ImportAgain(
            new ImportLeagueAgain(
                league.Id,
                snapshot,
                snapshot.Rosters.ToDictionary(r => r.RosterId, _ => Guid.NewGuid()),
                subject),
            now);

        stream.AppendMany(events);
        session.Store(SleeperSnapshot.Of(Guid.NewGuid(), responses, now));

        try
        {
            await session.SaveChangesAsync(cancellation);
        }
        catch (ConcurrencyException)
        {
            // Someone changed the league since it was read, perhaps by importing it at the same moment.
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "League changed",
                detail: $"{league.Name} changed while it was being imported. Look at the league, then import it again if a team is still missing.");
        }

        foreach (var @event in events)
        {
            league.Evolve(@event);
        }

        return Results.Ok(Summary(league, held, events));
    }

    private static ImportedLeague Summary(League league, Member held, IEnumerable<object> events) =>
        new(league.Id, league.Name, league.Season, league.Members.Count, events.OfType<MemberAdded>().Count(), held.MemberId, Roles.Of(held));

    private static IResult Incomplete(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Incomplete import", detail: detail);

    private static IResult NoSuchSleeperLeague() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "No such Sleeper league",
            detail: "Sleeper has no such league. Pick your league again.");

    private static IResult AlreadyImported() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Already imported",
            detail: "This league has already been imported. Find it under My leagues, or ask its treasurer for an invite.");
}
