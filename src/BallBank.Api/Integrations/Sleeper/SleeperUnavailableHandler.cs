using Microsoft.AspNetCore.Diagnostics;

namespace BallBank.Api.Integrations.Sleeper;

/// <summary>
/// Sleeper failing is not BallBank failing: <c>502 Bad Gateway</c> with a message that tells the person
/// to try again, instead of a <c>500</c>.
/// </summary>
public sealed class SleeperUnavailableHandler(IProblemDetailsService problemDetails, ILogger<SleeperUnavailableHandler> logger)
    : IExceptionHandler
{
    public const string Message = "Sleeper is not answering right now. Try again in a minute.";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellation)
    {
        if (exception is not SleeperUnavailableException unavailable)
        {
            return false;
        }

        logger.LogWarning(unavailable, "Sleeper is unavailable");

        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = unavailable,
            ProblemDetails = { Status = StatusCodes.Status502BadGateway, Title = "Sleeper is unavailable", Detail = Message },
        });
    }
}
