using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

/// <summary>
/// Creates an account-specific private provider without retaining credentials in DI.
/// </summary>
public sealed class BybitPrivateAccountProviderFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public BybitPrivateAccountProviderFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IPrivateAccountProvider Create(BybitCredentials credentials) =>
        new BybitPrivateAccountProvider(
            BybitClientFactory.CreatePrivateClient(credentials),
            _loggerFactory.CreateLogger<BybitPrivateAccountProvider>());
}
