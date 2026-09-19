using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed record ExchangeAccountVerificationResult(
    ExchangeAccountVerificationOutcome Outcome,
    ExchangeAccount? Account);
