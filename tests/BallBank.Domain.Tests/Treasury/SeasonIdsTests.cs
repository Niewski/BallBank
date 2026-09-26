using BallBank.Domain.Treasury;

namespace BallBank.Domain.Tests.Treasury;

public class SeasonIdsTests
{
    [Fact]
    public void The_same_league_and_label_always_name_the_same_season()
    {
        var leagueId = Guid.NewGuid();

        SeasonIds.SeasonId(leagueId, "2026").ShouldBe(SeasonIds.SeasonId(leagueId, "2026"));
    }

    [Fact]
    public void A_different_label_names_a_different_season()
    {
        var leagueId = Guid.NewGuid();

        SeasonIds.SeasonId(leagueId, "2026").ShouldNotBe(SeasonIds.SeasonId(leagueId, "2027"));
    }

    [Fact]
    public void A_different_league_names_a_different_season_for_the_same_label()
    {
        SeasonIds.SeasonId(Guid.NewGuid(), "2026").ShouldNotBe(SeasonIds.SeasonId(Guid.NewGuid(), "2026"));
    }

    [Fact]
    public void The_same_season_and_member_always_name_the_same_account()
    {
        var seasonId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        SeasonIds.AccountId(seasonId, memberId).ShouldBe(SeasonIds.AccountId(seasonId, memberId));
    }

    [Fact]
    public void Different_members_of_the_same_season_get_different_accounts()
    {
        var seasonId = Guid.NewGuid();

        SeasonIds.AccountId(seasonId, Guid.NewGuid()).ShouldNotBe(SeasonIds.AccountId(seasonId, Guid.NewGuid()));
    }

    [Fact]
    public void The_same_season_always_names_the_same_dues_assessment()
    {
        var seasonId = Guid.NewGuid();

        SeasonIds.DuesAssessmentId(seasonId).ShouldBe(SeasonIds.DuesAssessmentId(seasonId));
    }

    [Fact]
    public void Different_seasons_name_different_dues_assessments()
    {
        SeasonIds.DuesAssessmentId(Guid.NewGuid()).ShouldNotBe(SeasonIds.DuesAssessmentId(Guid.NewGuid()));
    }
}
