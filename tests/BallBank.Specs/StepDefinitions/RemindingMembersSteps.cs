using System.Globalization;
using BallBank.Specs.Support;
using Reqnroll;

namespace BallBank.Specs.StepDefinitions;

[Binding]
public sealed class RemindingMembersSteps(LeagueWorld world)
{
    // Two in the afternoon UTC is ten in the morning Eastern, outside the quiet hours a member has until they choose others.
    private static readonly TimeSpan Afternoon = TimeSpan.FromHours(14);

    private ILeagueDriver League => world.Driver;

    [When(@"^the tick runs on (\d{4}-\d{2}-\d{2})$")]
    public Task WhenTheTickRunsOn(string date) => League.RunTick(At(date, Afternoon));

    [When(@"^the tick runs on (\d{4}-\d{2}-\d{2}) at (\d{2}:\d{2})$")]
    public Task WhenTheTickRunsOnAt(string date, string time) =>
        League.RunTick(At(date, TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan()));

    [When(@"^the tick runs now$")]
    public Task WhenTheTickRunsNow() => League.RunTick(League.Now);

    // Relative to the league's own clock, so quiet hours put an hour either side of "now" stay where the scenario means them.
    [When(@"^the tick runs (\d+) hours? later$")]
    public Task WhenTheTickRunsLater(int hours) => League.RunTick(League.Now.AddHours(hours));

    [Then(@"^the dashboard shows no reminder for (\w+)$")]
    public async Task ThenNoReminderShown(string member) => (await League.LastReminder(member)).ShouldBeNull();

    [Then(@"^the dashboard shows (\w+)'s last reminder as (Sent|Held|Skipped)$")]
    public async Task ThenLastReminderShown(string member, string status) => (await League.LastReminder(member)).ShouldBe(status);

    private static DateTimeOffset At(string date, TimeSpan timeOfDay) =>
        new DateTimeOffset(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) + timeOfDay;
}
