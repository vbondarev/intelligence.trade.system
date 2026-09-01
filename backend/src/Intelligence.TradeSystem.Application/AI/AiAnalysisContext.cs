using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.AI;

public sealed record AiAnalysisContext
{
    public required MarketSnapshot Market { get; init; }

    public required PortfolioSnapshot Portfolio { get; init; }
}
