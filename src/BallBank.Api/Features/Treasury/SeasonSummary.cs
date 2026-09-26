using BallBank.Domain.Treasury;

namespace BallBank.Api.Features.Treasury;

/// <summary>What opening a season, or listing a league's seasons, answers.</summary>
public sealed record SeasonSummary(Guid SeasonId, string Label, string Status, decimal DuesAmount, DateOnly DueDate)
{
    public static SeasonSummary Of(Season season) => new(season.Id, season.Label, SeasonStatus.Open, season.DuesAmount, season.DueDate);

    public static SeasonSummary Of(SeasonListing season) => new(season.Id, season.Label, SeasonStatus.Open, season.DuesAmount, season.DueDate);
}

/// <summary>A season's status. Only <see cref="Open"/> exists so far; closing a season is planned.</summary>
public static class SeasonStatus
{
    public const string Open = "Open";
}
