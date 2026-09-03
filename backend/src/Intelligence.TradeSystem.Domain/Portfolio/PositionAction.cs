namespace Intelligence.TradeSystem.Domain.Portfolio;

/// <summary>Действие с текущей позицией; не является разрешением на увеличение риска.</summary>
public enum PositionAction
{
    Hold,
    ProtectProfit,
    Reduce,
    Close,
    MoveStop,
    TakePartialProfit
}
