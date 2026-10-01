using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class PostingTheWeeklyDigestSteps(LeagueWorld world)
{
    private ILeagueDriver League => world.Driver;

    [Given(@"^the treasurer has connected Discord, posting the weekly digest$")]
    public Task GivenTheTreasurerHasConnectedDiscordPostingTheDigest() => League.ConnectDiscord(announcePayments: false, postDigest: true);

    [Given(@"^the treasurer has disconnected Discord$")]
    public Task GivenTheTreasurerHasDisconnectedDiscord() => League.DisconnectDiscord();

    [Then(@"^Discord was told no weekly digest$")]
    public async Task ThenDiscordWasToldNoDigest() => (await WeeklyDigests()).ShouldBeEmpty();

    [Then(@"^Discord was told (\d+) weekly digests?$")]
    public async Task ThenDiscordWasToldDigests(int count) => (await WeeklyDigests()).Count.ShouldBe(count);

    // A digest is several lines, so a scenario writes it a line to a row.
    [Then(@"^the latest weekly digest in Discord reads:$")]
    public async Task ThenTheLatestDigestReads(DataTable lines) =>
        (await WeeklyDigests()).LastOrDefault().ShouldBe(string.Join("\n", lines.Rows.Select(row => row[0])));

    private async Task<List<string>> WeeklyDigests() =>
        [.. (await League.PostedToDiscord()).Where(message => message.StartsWith("Weekly digest for ", StringComparison.Ordinal))];
}
