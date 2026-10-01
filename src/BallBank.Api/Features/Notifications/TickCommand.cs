using JasperFx.CommandLine;

namespace BallBank.Api.Features.Notifications;

/// <summary>
/// <c>dotnet BallBank.Api.dll tick</c>: the Container Apps Job's entry point (ADR-0007, docs/runbook.md). Does the
/// scheduled work due as of now (<see cref="Tick"/>) and exits, with a failure code when a league could not be finished
/// or a send was refused, so the Job shows as failed. The host is built and not started: the Job serves nothing, and runs neither the
/// projection daemon nor the queues the API's node owns.
/// </summary>
[Description("Sends the due-date reminders due now, releases held texts whose quiet hours have ended, and exits.")]
public sealed class TickCommand : JasperFxAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();
        var report = await host.Services.GetRequiredService<Tick>().RunAsync();
        return report.Succeeded;
    }
}
