namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

/// <summary>Список текущих подключений пользователя.</summary>
public sealed record ExchangeAccountListResponse(
    IReadOnlyList<ExchangeAccountResponse> Items);
