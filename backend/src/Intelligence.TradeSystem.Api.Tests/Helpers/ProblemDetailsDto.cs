namespace Intelligence.TradeSystem.Api.Tests.Helpers;

internal sealed class ProblemDetailsDto
{
    public string? Type { get; init; }

    public int? Status { get; init; }

    public string? Title { get; init; }

    public string? Detail { get; init; }

    public string? Code { get; init; }

    public string? TraceId { get; init; }
}
