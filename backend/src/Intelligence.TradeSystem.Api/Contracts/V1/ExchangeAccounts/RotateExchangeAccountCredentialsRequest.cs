using System.ComponentModel.DataAnnotations;

namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

/// <summary>Запрос на полную замену credentials биржевого аккаунта.</summary>
public sealed record RotateExchangeAccountCredentialsRequest
{
    /// <summary>Новый API key.</summary>
    [Required]
    public required string ApiKey { get; init; }

    /// <summary>Новый API secret.</summary>
    [Required]
    public required string ApiSecret { get; init; }
}
