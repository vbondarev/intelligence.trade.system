using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed record ExchangeAccountConnectionResult(
    ExchangeAccountConnectionOutcome Outcome,
    ExchangeAccount? Account)
{
    public static ExchangeAccountConnectionResult Connected(ExchangeAccount account) =>
        new(ExchangeAccountConnectionOutcome.Connected, account);

    public static ExchangeAccountConnectionResult Failed(ExchangeAccountConnectionOutcome outcome) =>
        new(outcome, null);
}
