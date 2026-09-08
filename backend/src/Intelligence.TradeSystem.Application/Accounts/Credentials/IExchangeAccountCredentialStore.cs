using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// User-scoped storage boundary for exchange credential pairs.
/// </summary>
public interface IExchangeAccountCredentialStore
{
    /// <summary>
    /// Reads credentials owned by <paramref name="userId"/>. Missing and foreign rows
    /// have the same observable result.
    /// </summary>
    Task<ExchangeAccountCredential?> GetAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads only technical metadata for a stored pair without decrypting it.
    /// Missing and foreign rows have the same observable result.
    /// </summary>
    Task<ExchangeAccountCredentialMetadata?> GetMetadataAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates credentials for an existing account without replacing an existing pair.
    /// </summary>
    Task<ConcurrencyVersion> CreateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the complete credential pair using compare-and-swap.
    /// </summary>
    Task<ConcurrencyVersion> RotateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        ExchangeAccountCredentialSecret replacement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the locally stored pair using compare-and-swap. This does not revoke
    /// the corresponding key at the exchange.
    /// </summary>
    Task RevokeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encrypts the same pair with the currently active protection key using
    /// compare-and-swap.
    /// </summary>
    Task<ConcurrencyVersion> ReprotectAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);
}
