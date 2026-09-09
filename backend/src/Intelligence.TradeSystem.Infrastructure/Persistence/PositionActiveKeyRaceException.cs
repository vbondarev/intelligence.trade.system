using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

internal sealed class PositionActiveKeyRaceException(Exception innerException)
    : Exception(
        "A synchronization attempt lost a concurrent active position insert race.",
        innerException),
        IPositionActiveKeyViolation;
