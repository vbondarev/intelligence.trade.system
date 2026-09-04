namespace Intelligence.TradeSystem.Application.Concurrency;

/// <summary>
/// Выбрасывается, когда сохранение мутируемого агрегата проиграло гонку оптимистической
/// конкурентности: ожидаемая версия не совпала с фактической (или ожидалась вставка новой
/// строки, а строка уже существует).
/// </summary>
/// <remarks>
/// Application-слой не выполняет повторных попыток (no retry): вызывающий код сам решает,
/// перечитывать ли агрегат и повторять операцию.
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
