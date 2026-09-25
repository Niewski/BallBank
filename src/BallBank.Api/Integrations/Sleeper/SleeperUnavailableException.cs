namespace BallBank.Api.Integrations.Sleeper;

/// <summary>Sleeper could not be reached, was too slow, or answered with something other than an answer.</summary>
public sealed class SleeperUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
