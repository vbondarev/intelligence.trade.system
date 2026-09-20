namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions;

/// <summary>Состояние отслеживания позиции в пользовательском API.</summary>
public enum PositionTrackingStateV1
{
    Active,
    Unknown,
    Stale,
    Closed,
}
