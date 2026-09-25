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
/// <param name="DisplayName">What the importer wants to be called in this league.</param>
public sealed record ImportLeagueRequest(string SleeperLeagueId, string SleeperUsername, string DisplayName);

/// <summary>The league an import created, as its importer now holds it.</summary>
public sealed record ImportedLeague(
    Guid LeagueId,
    string Name,
    string Season,
    int Members,
    Guid MemberId,
    IReadOnlyList<string> Roles);

public static class ImportLeagueEndpoint
{
    /// <summary>
    /// Brings a Sleeper league into BallBank as <paramref name="leagueId"/>, which the client chooses.
    /// Everything Sleeper is asked happens before anything is written, and everything written (the
    /// league stream, the raw Sleeper responses, the Sleeper league index and the importer's
    /// membership) commits in one transaction, so a failure part-way leaves nothing behind.
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
        if (string.IsNullOrWhiteSpace(request.SleeperLeagueId)
            || string.IsNullOrWhiteSpace(request.SleeperUsername)
            || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Incomplete import",
                detail: "Importing a league needs the Sleeper league, your Sleeper username and the name you go by.");
        }

        // The session is opened on the canonical form of the league id, so the tenant never depends on
        // how the client spelled the GUID. It is committed explicitly below, which is why this endpoint
        // takes the store rather than a session Wolverine would commit for it.
        await using var session = store.LightweightSession(leagueId.ToString());

        var existing = await session.Events.AggregateStreamAsync<League>(leagueId, token: cancellation);
        if (existing is not null)
        {
            // The same import again, as a retry after a lost response is, answers with the league it
            // made and writes nothing. Importing again to pick up new teams is #21.
            return existing.SleeperLeagueId == request.SleeperLeagueId.Trim() && existing.MemberHeldBy(user.Subject()) is { } held
                ? Results.Ok(Summary(existing, held))
                : AlreadyImported();
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
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No such Sleeper league",
                detail: "Sleeper has no such league. Pick your league again.");
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
        var subject = user.Subject();
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
        var summary = Summary(league, importersMember);

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

    private static ImportedLeague Summary(League league, Member held) =>
        new(league.Id, league.Name, league.Season, league.Members.Count, held.MemberId, Roles.Of(held));

    private static IResult AlreadyImported() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Already imported",
            detail: "This league has already been imported. Find it under My leagues, or ask its treasurer for an invite.");
}
