using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Evaluations;

/// <summary>
/// Атомарная граница сохранения assessment, recommendation state и evaluation event.
/// Внешние market/exchange calls должны завершиться до входа в эту границу.
/// </summary>
public interface IPositionEvaluationTransaction
{
    Task ExecuteAsync(
        UserId userId,
        PositionId positionId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
