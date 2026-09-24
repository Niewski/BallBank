namespace BallBank.Domain;

/// <summary>
/// A command was refused because it would break an invariant. The message is written for the person
/// who issued the command (a member or treasurer), so it is safe to surface as-is.
/// </summary>
public sealed class DomainException(string message) : Exception(message)
{
}
