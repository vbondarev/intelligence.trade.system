using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intelligence.TradeSystem.Api.Errors;

internal static class ApiProblemDetails
{
    public static ProblemDetails Create(
        HttpContext httpContext,
        ApiErrorDescriptor descriptor,
        string? detail = null)
    {
        var problemDetails = new ProblemDetails
        {
            Type = descriptor.Type,
            Title = descriptor.Title,
            Status = descriptor.StatusCode,
            Detail = detail,
        };

        problemDetails.Extensions["code"] = descriptor.Code;
        AddTraceId(problemDetails, httpContext);
        return problemDetails;
    }

    public static void Customize(ProblemDetailsContext context)
    {
        AddTraceId(context.ProblemDetails, context.HttpContext);

        if (context.ProblemDetails.Status == StatusCodes.Status400BadRequest)
        {
            context.ProblemDetails.Type ??= ApiErrorDescriptors.ValidationFailed.Type;
            context.ProblemDetails.Title ??= ApiErrorDescriptors.ValidationFailed.Title;
            context.ProblemDetails.Extensions.TryAdd(
                "code",
                ApiErrorDescriptors.ValidationFailed.Code);
        }
    }

    public static ProblemDetails CreateValidation(HttpContext httpContext, string detail) =>
        Create(httpContext, ApiErrorDescriptors.ValidationFailed, detail);

    public static void AddModelStateErrors(
        ProblemDetails problemDetails,
        ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(pair => pair.Value is not null && pair.Value.Errors.Count > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The value is invalid."
                        : error.ErrorMessage)
                    .ToArray());

        problemDetails.Extensions["errors"] = errors;
    }

    private static void AddTraceId(
        ProblemDetails problemDetails,
        HttpContext httpContext)
    {
        problemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }
}
