using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BallBank.Api;
using BallBank.Api.Features.Membership;
using BallBank.Api.Features.Treasury;
using BallBank.Domain;
using BallBank.Domain.Treasury;
using BallBank.Integration.Tests.Sleeper;
using Marten;

namespace BallBank.Specs.Support;

/// <summary>
/// The league as the API keeps it, driven over HTTP by the people in it. Holland Hogs is served by the
/// fake Sleeper with only the teams of the members named, imported by Jacob (who keeps its books) and
/// claimed by everyone else through an invite. Every command goes through the Treasury endpoints with
/// a fresh <c>Idempotency-Key</c> and the version last read; balances and the pot are read from the
/// statements and the ledger, the history from the event store.
/// </summary>
public sealed class HttpLeagueDriver : ILeagueDriver
{
    /// <summary>Who imports the league, and so is its treasurer.</summary>
    public const string Treasurer = "Jacob";

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly Guid _leagueId = Guid.NewGuid();
    private readonly string _sleeperLeagueId = Random.Shared.NextInt64(910000000000000000, 999999999999999999).ToString();
    private readonly JsonArray _sleeperUsers = [];
    private readonly JsonArray _sleeperRosters = [];
    private readonly Dictionary<string, string> _subjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _memberIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AttestPaymentRequest> _latestAttestation = new(StringComparer.OrdinalIgnoreCase);

    private SpecsHost? _host;
    private string? _season;

    public Guid TreasurerId => MemberId(Treasurer);

    /// <summary>The last command sent, and what it was answered.</summary>
    public (SentRequest Request, Answer Answer)? Last { get; private set; }

    private SpecsHost Host => _host ?? throw new InvalidOperationException("Open the league first.");

    public async Task OpenLeague(IReadOnlyList<string> members)
    {
        if (!members.Contains(Treasurer, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Over HTTP, {Treasurer} imports the league, so must be one of its members.");
        }

        _host = await SpecsHost.Shared();
        foreach (var member in members)
        {
            JoinOnSleeper(member);
        }

        ServeOnSleeper();

        await Send(Treasurer, HttpMethod.Post, $"/leagues/{_leagueId}/import", new ImportLeagueRequest(_sleeperLeagueId, "jacob", Treasurer));
        await LearnMemberIds();

        foreach (var member in members.Where(m => !m.Equals(Treasurer, StringComparison.OrdinalIgnoreCase)))
        {
            await Claim(member);
        }
    }

    public async Task OpenSeason(string label, decimal duesAmount, DateOnly dueDate)
    {
        await Send(Treasurer, HttpMethod.Post, $"/leagues/{_leagueId}/seasons", new OpenSeasonRequest(label, duesAmount, dueDate));
        _season = label;
    }

    /// <summary>The member's team joins on Sleeper, and the treasurer imports the league again. Nobody claims it yet.</summary>
    public async Task AddMember(string member)
    {
        JoinOnSleeper(member);
        ServeOnSleeper();

        await Send(Treasurer, HttpMethod.Post, $"/leagues/{_leagueId}/import", new ImportLeagueRequest());
        await LearnMemberIds();
    }

    public Task Assess(string member, decimal amount, string memo, DateOnly dueDate) =>
        Send(
            Treasurer,
            HttpMethod.Post,
            $"/leagues/{_leagueId}/seasons/{Season}/assessments",
            new AssessmentRequest(Guid.NewGuid(), amount, dueDate, memo, [MemberId(member)]));

    public async Task Attest(string member, decimal amount, PaymentRail rail, string? reference, bool byTreasurer = false)
    {
        var request = new AttestPaymentRequest(Guid.NewGuid(), amount, rail, reference, (await Statement(member)).Version);
        _latestAttestation[member] = request;
        await Send(byTreasurer ? Treasurer : member, HttpMethod.Post, $"/leagues/{_leagueId}/accounts/{AccountId(member)}/attestations", request);
    }

    public async Task AttestSameAgain(string member)
    {
        var request = LatestAttestation(member) with { Version = (await Statement(member)).Version };
        await Send(member, HttpMethod.Post, $"/leagues/{_leagueId}/accounts/{AccountId(member)}/attestations", request);
    }

    public async Task ConfirmLatest(string member) =>
        await Send(Treasurer, HttpMethod.Post, ConfirmationPath(member), new ConfirmPaymentRequest((await Statement(member)).Version));

    public async Task RejectLatest(string member, string reason) =>
        await Send(
            Treasurer,
            HttpMethod.Post,
            $"/leagues/{_leagueId}/accounts/{AccountId(member)}/attestations/{LatestAttestation(member).AttestationId}/rejection",
            new RejectPaymentRequest(reason, (await Statement(member)).Version));

    public async Task PostAdjustment(string member, decimal amount, string reason, bool refund = false, string? postedBy = null) =>
        await Send(
            postedBy ?? Treasurer,
            HttpMethod.Post,
            $"/leagues/{_leagueId}/accounts/{AccountId(member)}/adjustments",
            new PostAdjustmentRequest(Guid.NewGuid(), amount, reason, (await Statement(member)).Version, refund));

    public async Task<decimal> Balance(string member) => (await Statement(member)).Balance;

    public async Task<int> PendingPayments(string member) =>
        (await Statement(member)).Lines.Count(l => l is { Kind: StatementLineKind.Attestation, Status: nameof(AttestationStatus.Pending) });

    /// <summary>Every account on the season's ledger: what was confirmed paid in, less what was refunded.</summary>
    public async Task<decimal> Pot()
    {
        var pot = 0m;
        foreach (var entry in await Get<LedgerEntry[]>(Treasurer, $"/leagues/{_leagueId}/seasons/{Season}/ledger"))
        {
            var statement = await Get<AccountStatement>(Treasurer, $"/leagues/{_leagueId}/accounts/{entry.AccountId}");
            pot += statement.Totals.Confirmed - statement.Lines.Where(l => l.Refund).Sum(l => l.Amount);
        }

        return pot;
    }

    public async Task<IReadOnlyList<object>> AccountHistory(string member)
    {
        await using var session = Host.Store.QuerySession(_leagueId.ToString());
        return (await session.Events.FetchStreamAsync(AccountId(member))).Select(e => e.Data).ToList();
    }

    public async Task<IReadOnlyList<object>> History()
    {
        await using var session = Host.Store.QuerySession(_leagueId.ToString());
        return (await session.Events.QueryAllRawEvents().OrderBy(e => e.Sequence).ToListAsync()).Select(e => e.Data).ToList();
    }

    /// <summary>The treasurer makes the member a treasurer too.</summary>
    public Task AppointTreasurer(string member) =>
        Send(Treasurer, HttpMethod.Post, $"/leagues/{_leagueId}/treasurers", new AppointTreasurerRequest(MemberId(member)));

    /// <summary>
    /// Each treasurer confirms the member's latest payment, all from the same read of the account and
    /// all in flight together, none committing before every one has read.
    /// </summary>
    public async Task<Answer[]> ConfirmAtOnce(string member, IReadOnlyList<string> treasurers)
    {
        var body = JsonSerializer.Serialize(new ConfirmPaymentRequest((await Statement(member)).Version), Json);

        using var _ = Host.Commits.Hold(_leagueId, treasurers.Count);
        return await Task.WhenAll(treasurers.Select(treasurer =>
            SendRaw(new SentRequest(treasurer, HttpMethod.Post, ConfirmationPath(member), body, Guid.NewGuid().ToString()))));
    }

    /// <summary>Sends the last command again, key and all, as an app retrying after a lost answer would.</summary>
    public async Task<(Answer First, Answer Again)> RetryLast()
    {
        var (request, first) = Last ?? throw new InvalidOperationException("Nothing has been sent yet.");
        return (first, await SendRaw(request));
    }

    /// <summary>The account's version now.</summary>
    public async Task<int> Version(string member) => (await Statement(member)).Version;

    private string Season => _season ?? throw new InvalidOperationException("No season is open yet.");

    private Guid MemberId(string member) =>
        _memberIds.TryGetValue(member, out var id) ? id : throw new KeyNotFoundException($"No member named '{member}' in this league.");

    private Guid AccountId(string member) => SeasonIds.AccountId(SeasonIds.SeasonId(_leagueId, Season), MemberId(member));

    private string ConfirmationPath(string member) =>
        $"/leagues/{_leagueId}/accounts/{AccountId(member)}/attestations/{LatestAttestation(member).AttestationId}/confirmation";

    private AttestPaymentRequest LatestAttestation(string member) =>
        _latestAttestation.TryGetValue(member, out var request)
            ? request
            : throw new InvalidOperationException($"{member} has not attested a payment yet.");

    /// <summary>The account as the treasurer reads it.</summary>
    private Task<AccountStatement> Statement(string member) =>
        Get<AccountStatement>(Treasurer, $"/leagues/{_leagueId}/accounts/{AccountId(member)}");

    private string Subject(string person)
    {
        if (!_subjects.TryGetValue(person, out var subject))
        {
            subject = $"test|{person.ToLowerInvariant()}-{Guid.NewGuid():N}";
            _subjects[person] = subject;
        }

        return subject;
    }

    /// <summary>The treasurer invites the member, who claims them.</summary>
    private async Task Claim(string member)
    {
        var inviteId = Guid.NewGuid();
        await Send(Treasurer, HttpMethod.Post, $"/leagues/{_leagueId}/members/{MemberId(member)}/invites", new IssueInviteRequest(inviteId));
        await Send(
            member,
            HttpMethod.Post,
            $"/leagues/{_leagueId}/claims",
            new ClaimMemberRequest(inviteId, member, $"{member.ToLowerInvariant()}@example.com"));
    }

    private async Task LearnMemberIds()
    {
        var league = await Get<LeagueMembers>(Treasurer, $"/leagues/{_leagueId}/members");
        foreach (var member in league.Members.Where(m => m.SleeperDisplayName is not null))
        {
            _memberIds[member.SleeperDisplayName!] = member.MemberId;
        }
    }

    /// <summary>The member's user and team from the Holland Hogs fixtures join this league on Sleeper, without co-owners.</summary>
    private void JoinOnSleeper(string member)
    {
        var user = SleeperFixture("league-users.json")
            .SingleOrDefault(u => string.Equals((string?)u!["display_name"], member, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Holland Hogs on Sleeper has no user called '{member}'; the specs use Jacob, Sam and Priya.");
        var roster = SleeperFixture("league-rosters.json").Single(r => (string?)r!["owner_id"] == (string?)user["user_id"])!;

        user["league_id"] = _sleeperLeagueId;
        roster["league_id"] = _sleeperLeagueId;
        roster["co_owners"] = null;
        _sleeperUsers.Add(user.DeepClone());
        _sleeperRosters.Add(roster.DeepClone());
    }

    private void ServeOnSleeper()
    {
        var league = File.ReadAllText(SleeperFixturePath("league.json")).Replace(FakeSleeper.HollandHogsLeagueId, _sleeperLeagueId);
        Host.Sleeper.ServeJson($"/league/{_sleeperLeagueId}", league);
        Host.Sleeper.ServeJson($"/league/{_sleeperLeagueId}/users", _sleeperUsers.ToJsonString());
        Host.Sleeper.ServeJson($"/league/{_sleeperLeagueId}/rosters", _sleeperRosters.ToJsonString());
    }

    private static JsonArray SleeperFixture(string fixture) => JsonNode.Parse(File.ReadAllText(SleeperFixturePath(fixture)))!.AsArray();

    private static string SleeperFixturePath(string fixture) => Path.Combine(AppContext.BaseDirectory, "Sleeper", "Fixtures", fixture);

    private async Task<T> Get<T>(string person, string path)
    {
        using var client = Host.ClientFor(Subject(person));
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>
    /// Sends a command as <paramref name="person"/> under a fresh key. A refusal is thrown as the
    /// <see cref="DomainException"/> it was in the API, a <c>403</c> as <see cref="NotAllowedException"/>,
    /// and anything else unsuccessful fails the scenario.
    /// </summary>
    private async Task<Answer> Send(string person, HttpMethod method, string path, object body)
    {
        var request = new SentRequest(person, method, path, JsonSerializer.Serialize(body, body.GetType(), Json), Guid.NewGuid().ToString());
        var answer = await SendRaw(request);
        Last = (request, answer);

        return answer.Status switch
        {
            HttpStatusCode.Conflict when answer.Problem() is { Title: "Refused" } refused => throw new DomainException(refused.Detail ?? ""),
            HttpStatusCode.Forbidden => throw new NotAllowedException(person),
            _ when (int)answer.Status is >= 200 and < 300 => answer,
            _ => throw new InvalidOperationException($"{method} {path} as {person} was answered {(int)answer.Status}: {answer.Body}"),
        };
    }

    private async Task<Answer> SendRaw(SentRequest request)
    {
        using var client = Host.ClientFor(Subject(request.Person));
        using var message = new HttpRequestMessage(request.Method, request.Path)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Add(Idempotency.Header, request.IdempotencyKey);

        using var response = await client.SendAsync(message);
        return new Answer(response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}

/// <summary>A command as sent: who by, where, the body and its <c>Idempotency-Key</c>.</summary>
public sealed record SentRequest(string Person, HttpMethod Method, string Path, string Body, string IdempotencyKey);

/// <summary>What the API answered.</summary>
public sealed record Answer(HttpStatusCode Status, string Body)
{
    /// <summary>The problem answered; <c>null</c> when the body is not one.</summary>
    public ProblemAnswer? Problem()
    {
        try
        {
            return JsonSerializer.Deserialize<ProblemAnswer>(Body, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A problem the API answered with; <paramref name="CurrentVersion"/> on a version conflict.</summary>
public sealed record ProblemAnswer(string? Type, string? Title, string? Detail, int? CurrentVersion);
