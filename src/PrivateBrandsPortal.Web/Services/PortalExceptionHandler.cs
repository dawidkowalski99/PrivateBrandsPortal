using Microsoft.AspNetCore.Diagnostics;
namespace PrivateBrandsPortal.Web.Services;

public sealed class PortalExceptionHandler(ILogger<PortalExceptionHandler> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Exclude exception messages, request data, SQL and credentials.
        logger.LogError("Unhandled {ExceptionType}. Request ID: {RequestId}", exception.GetType().Name, context.TraceIdentifier);
        return ValueTask.FromResult(false); // Re-execute the safe MVC error page.
    }
}
