namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Помечает ошибку ограничения активного ключа биржевой позиции, чтобы синхронизация могла
/// отличить подтверждённую гонку вставки от обычной ошибки сохранения.
/// </summary>
public interface IPositionActiveKeyViolation;
