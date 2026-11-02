namespace Intelligence.TradeSystem.Application.Realtime;

public sealed class MarketPriceSubscriptionStartUnconfirmedException(
    Exception? innerException = null,
    IAsyncDisposable? unconfirmedLease = null)
    : Exception(
        "Не удалось подтвердить безопасное состояние первоначального создания публичной рыночной подписки.",
        innerException)
{
    public IAsyncDisposable? UnconfirmedLease { get; } = unconfirmedLease;
}
