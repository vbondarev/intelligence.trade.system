using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Market;
using Microsoft.AspNetCore.Diagnostics;

namespace Intelligence.TradeSystem.Api.Errors;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (IsRequestAbortedCancellation(httpContext, exception))
        {
            return true;
        }

        var (descriptor, detail) = exception switch
        {
            ConcurrencyConflictException => (ApiErrorDescriptors.ConcurrencyConflict, "The resource was modified by another operation."),
            MarketDataUnavailableException => (ApiErrorDescriptors.MarketDataUnavailable, null),
            DataSourceException => (ApiErrorDescriptors.MarketDataUnavailable, null),
            // The current market endpoints use these framework exceptions for
            // user-controlled exchange/symbol constraints; keep that API behavior
            // explicit until those constraints have dedicated application errors.
            ArgumentException or NotSupportedException => (ApiErrorDescriptors.ValidationFailed, exception.Message),
            _ => (ApiErrorDescriptors.InternalError, null),
        };

        httpContext.Response.StatusCode = descriptor.StatusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = ApiProblemDetails.Create(httpContext, descriptor, detail),
        });

        return true;
    }

    internal static bool ShouldSuppressDiagnostics(
        Exception exception,
        bool requestAborted)
    {
        if (requestAborted && exception is OperationCanceledException)
        {
            return true;
        }

        return exception is ConcurrencyConflictException
            or MarketDataUnavailableException
            or DataSourceException
            or ArgumentException
            or NotSupportedException;
    }

    private static bool IsRequestAbortedCancellation(
        HttpContext httpContext,
        Exception exception) =>
        exception is OperationCanceledException
        && httpContext.RequestAborted.IsCancellationRequested;
}
