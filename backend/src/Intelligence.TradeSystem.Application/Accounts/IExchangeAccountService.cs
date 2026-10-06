using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountService
{
    Task<IReadOnlyList<ExchangeAccount>> ListActiveAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает все подключения пользователя, включая отключённые, для management-сценария.
    /// </summary>
    Task<IReadOnlyList<ExchangeAccount>> ListAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подключает новый provider-side аккаунт или восстанавливает ранее отключённое подключение.
    /// </summary>
    /// <param name="displayName">
    /// Отображаемое имя нового подключения. При восстановлении отключённого подключения
    /// сохраняется прежнее имя: этот аргумент не является неявным переименованием.
    /// </param>
    Task<ExchangeAccountConnectionResult> ConnectAsync(
        UserId userId,
        ExchangeId exchange,
        string displayName,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменяет отображаемое имя подключения пользователя в любом lifecycle state.
    /// </summary>
    /// <returns>
    /// Актуальное подключение либо <c>null</c>, если оно отсутствует в области пользователя.
    /// </returns>
    Task<ExchangeAccount?> RenameAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccount?> DisconnectAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccountVerificationResult> VerifyAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccountCredentialRotationResult> RotateCredentialsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret replacement,
        CancellationToken cancellationToken = default);
}
