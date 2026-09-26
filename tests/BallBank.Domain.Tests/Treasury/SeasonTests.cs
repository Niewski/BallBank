using BallBank.Domain.Treasury;

namespace BallBank.Domain.Tests.Treasury;

public class SeasonTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DueDate = new(2026, 10, 1);
    private static readonly Guid LeagueId = Guid.NewGuid();
    private static readonly Guid Treasurer = Guid.NewGuid();

    private static OpenSeason Command(string label = "2026", decimal amount = 50m, DateOnly? dueDate = null) =>
        new(Guid.NewGuid(), LeagueId, label, amount, dueDate ?? DueDate, Treasurer);

    [Fact]
    public void Opening_a_season_records_its_label_dues_and_due_date()
    {
        var command = Command();

        var opened = Season.Open(command, existing: null, Now);

        opened.ShouldBe(new SeasonOpened(command.SeasonId, LeagueId, "2026", 50m, DueDate, Treasurer, Now));
    }

    [Fact]
    public void Opening_a_season_needs_a_label()
    {
        Should.Throw<DomainException>(() => Season.Open(Command(label: " "), existing: null, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Season_dues_must_be_a_positive_amount(int amount)
    {
        Should.Throw<DomainException>(() => Season.Open(Command(amount: amount), existing: null, Now));
    }

    [Fact]
    public void Opening_a_season_needs_a_due_date()
    {
        Should.Throw<DomainException>(() => Season.Open(Command() with { DueDate = null }, existing: null, Now));
    }

    [Fact]
    public void Opening_an_already_open_season_is_a_no_op()
    {
        var opened = Season.Open(Command(), existing: null, Now).ShouldNotBeNull();
        var season = Season.Replay(opened);

        var replay = Season.Open(Command(), existing: season, Now);

        replay.ShouldBeNull();
    }
}
