namespace BallBank.Api;

/// <summary>Response of <c>GET /v1/version</c>. The web app uses it as a reachability check.</summary>
public sealed record VersionInfo(string Name, string Version);
