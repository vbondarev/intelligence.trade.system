using System.ComponentModel.DataAnnotations;

namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

/// <summary>Запрос на подключение read-only биржевого аккаунта.</summary>
public sealed record CreateExchangeAccountRequest
{
    /// <summary>Провайдер биржи.</summary>
    public required ExchangeProvider Exchange { get; init; }

    /// <summary>API key для read-only доступа.</summary>
    [Required]
    public required string ApiKey { get; init; }

    /// <summary>API secret для read-only доступа.</summary>
    [Required]
    public required string ApiSecret { get; init; }
}
