using BallBank.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace BallBank.Api;

/// <summary>
/// A <see cref="DomainException"/> is a refused command, not a fault: <c>409 Conflict</c> with the
/// exception's message, which is written for the person who issued the command.
/// </summary>
public sealed class RefusalHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellation)
    {
        if (exception is not DomainException refusal)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = refusal,
            ProblemDetails = { Status = StatusCodes.Status409Conflict, Title = "Refused", Detail = refusal.Message },
        });
    }
}
