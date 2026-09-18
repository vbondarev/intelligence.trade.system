using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed record ExchangeAccountCredentialRotationResult(
    ExchangeAccountCredentialRotationOutcome Outcome,
    ExchangeAccount? Account);
