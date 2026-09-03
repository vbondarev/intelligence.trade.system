using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

internal sealed class BybitPrivateAccountProvider : IPrivateAccountProvider
{
    private readonly IBybitRestClient _client;
    private readonly ILogger<BybitPrivateAccountProvider> _logger;

    public BybitPrivateAccountProvider(IBybitRestClient client, ILogger<BybitPrivateAccountProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<OpenPositionsObservation> GetOpenPositionsAsync(MarketCategory category, string? symbol = null, CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
            throw new ArgumentException("Position data is not available for the Spot market. Use Linear or Inverse.", nameof(category));

        var observedAt = DateTimeOffset.UtcNow;
        var positions = new List<OpenPosition>();
        string? cursor = null;

        // Bybit paginates position lists via a cursor. A response is only a Complete snapshot
        // once every page has been fetched; an error mid-pagination can only ever downgrade to
        // Partial/Failed, never silently report an incomplete set as Complete.
        while (true)
        {
            var response = await _client.V5Api.Trading.GetPositionsAsync(
                category.ToBybitCategory(), symbol, null, null, 200, cursor, cancellationToken);

            if (!response.Success)
            {
                BybitPrivateProviderLogMessages.LogFailedToFetchOpenPositions(_logger, category, symbol ?? "all", response.Error?.Message);

                return positions.Count > 0
                    ? OpenPositionsObservation.Partial(category, symbol, observedAt, positions, response.Error?.Message)
                    : OpenPositionsObservation.Failed(
                        category, symbol, observedAt, response.Error?.Message ?? "Unknown Bybit API error.");
            }

            positions.AddRange(
                response.Data?.List?
                    .Where(position => position.Quantity > 0m)
                    .Select(position => position.MapOpenPosition(category))
                ?? []);

            cursor = response.Data?.NextPageCursor;
            if (string.IsNullOrEmpty(cursor))
                break;
        }

        return OpenPositionsObservation.Complete(category, symbol, observedAt, positions);
    }

    public async Task<AccountBalance?> GetWalletBalanceAsync(AccountType accountType, CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.Account.GetBalancesAsync(accountType.ToBybitAccountType(), null, cancellationToken);
        if (!response.Success)
        {
            BybitPrivateProviderLogMessages.LogFailedToFetchWalletBalance(_logger, accountType, response.Error?.Message);
            return null;
        }

        return response.Data?.List?.FirstOrDefault()?.MapAccountBalance();
    }
}
