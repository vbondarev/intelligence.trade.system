namespace Intelligence.TradeSystem.Api.Errors;

internal sealed record ApiErrorDescriptor(
    string Code,
    int StatusCode,
    string Title,
    string Type);
