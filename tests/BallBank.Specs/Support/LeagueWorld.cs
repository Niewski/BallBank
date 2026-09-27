using BallBank.Domain;

namespace BallBank.Specs.Support;

/// <summary>
/// The league the treasury specs act on, through the driver <see cref="SpecsDriver"/> selects, and what
/// the last attempted command was refused with. Injected into step classes by Reqnroll (one instance
/// per scenario).
/// </summary>
public sealed class LeagueWorld
{
    public ILeagueDriver Driver { get; } = SpecsDriver.IsHttp ? new HttpLeagueDriver() : new InMemoryLeagueDriver();

    /// <summary>Why the last <see cref="Attempt"/> was refused; <c>null</c> if it was not.</summary>
    public DomainException? Refusal { get; private set; }

    /// <summary>Who the last <see cref="Attempt"/> was not allowed to; <c>null</c> if it was allowed.</summary>
    public string? NotAllowed { get; private set; }

    /// <summary>Runs a command that may be refused, keeping the refusal for a later step to check.</summary>
    public async Task Attempt(Func<Task> command)
    {
        Refusal = null;
        NotAllowed = null;

        try
        {
            await command();
        }
        catch (DomainException refusal)
        {
            Refusal = refusal;
        }
        catch (NotAllowedException notAllowed)
        {
            NotAllowed = notAllowed.Member;
        }
    }
}
