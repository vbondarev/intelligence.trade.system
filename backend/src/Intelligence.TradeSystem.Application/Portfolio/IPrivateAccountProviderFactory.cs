using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Создаёт приватный провайдер для одной биржевой учётной записи, не раскрывая транспортные типы.
/// </summary>
public interface IPrivateAccountProviderFactory
{
    IPrivateAccountProviderLease Create(
        ExchangeId exchange,
        ExchangeAccountCredential credentials);
}
