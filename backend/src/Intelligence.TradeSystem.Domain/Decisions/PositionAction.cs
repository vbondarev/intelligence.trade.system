namespace Intelligence.TradeSystem.Domain.Decisions;

/// <summary>Действие с уже существующей позицией; не является разрешением на увеличение риска.</summary>
public enum PositionAction
{
    Hold,
    Watch,
    ProtectProfit,
    Reduce,
    Close,
    MoveStop,
    TakePartialProfit
}
