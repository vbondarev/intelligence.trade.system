namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Сторона совершённой сделки — покупатель-агрессор или продавец-агрессор.
/// Доменный аналог <c>Bybit.Net.Enums.OrderSide</c>; исключает зависимость
/// слоя Domain от внешних библиотек.
/// </summary>
public enum TradeSide
{
    /// <summary>Сделка инициирована агрессивным покупателем (taker buy).</summary>
    Buy,

    /// <summary>Сделка инициирована агрессивным продавцом (taker sell).</summary>
    Sell
}
