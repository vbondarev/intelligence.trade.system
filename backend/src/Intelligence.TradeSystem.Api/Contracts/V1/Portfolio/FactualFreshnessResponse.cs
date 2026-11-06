namespace Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;

public sealed record FactualFreshnessResponse(
    FactualFreshnessStatusV1 Status,
    DateTimeOffset? LastFullyReconciledAt);
