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

    public async Task<IReadOnlyList<OpenPosition>> GetOpenPositionsAsync(MarketCategory category, string? symbol = null, CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
            throw new ArgumentException("Position data is not available for the Spot market. Use Linear or Inverse.", nameof(category));

        var response = await _client.V5Api.Trading.GetPositionsAsync(category.ToBybitCategory(), symbol, null, null, 200, null, cancellationToken);
        if (!response.Success)
        {
            BybitPrivateProviderLogMessages.LogFailedToFetchOpenPositions(_logger, category, symbol ?? "all", response.Error?.Message);
            return [];
        }

        return response.Data?.List?.Where(position => position.Quantity > 0m).Select(position => position.MapOpenPosition(category)).ToList() ?? [];
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
