namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Marks an active exchange-position-key constraint failure so synchronization can
/// distinguish a verified insert race from an ordinary persistence error.
/// </summary>
public interface IPositionActiveKeyViolation;
