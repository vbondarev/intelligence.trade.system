namespace Intelligence.TradeSystem.Abstractions;

/// <summary>
/// Временный compatibility-контракт поверх нейтральных capability-интерфейсов Bybit.
/// Новые потребители должны зависеть от <see cref="IMarketDataProvider"/>,
/// <see cref="IDerivativesDataProvider"/> и <see cref="IPrivateAccountProvider"/>.
/// </summary>
/// <remarks>
/// Интерфейс сохранён для мягкой миграции существующих зависимостей.
/// После перевода потребителей на нейтральные capability-контракты должен быть удалён.
/// </remarks>
public interface IBybitProvider : IMarketDataProvider, IDerivativesDataProvider, IPrivateAccountProvider
{
}
