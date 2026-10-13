using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Создаёт приватный потоковый провайдер для конкретного аккаунта, не сохраняя учётные данные
/// в долгоживущих сервисах.
/// </summary>
public interface IExchangeRealtimeProviderFactory
{
    /// <exception cref="NotSupportedException">Биржа не поддерживает потоковое наблюдение.</exception>
    IExchangeRealtimeProvider Create(
        ExchangeId exchange,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredential credentials);
}
