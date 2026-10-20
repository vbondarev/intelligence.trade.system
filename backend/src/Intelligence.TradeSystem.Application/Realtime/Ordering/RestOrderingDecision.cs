using Intelligence.TradeSystem.Application.Time;

namespace Intelligence.TradeSystem.Application.Realtime.Ordering;

/// <summary>
/// Детерминированные правила пересечения потокового наблюдения с persisted REST-интервалами.
/// Интервалы не объединяются: промежуток между несмежными записями покрытием не считается.
/// </summary>
public static class RestOrderingDecision
{
    /// <summary>
    /// Строит интервал только при корректных границах. Перестановка timestamps запрещена: при
    /// <paramref name="startedAt"/> &gt; <paramref name="completedAt"/> результат
    /// <see langword="null"/>.
    /// </summary>
    public static (DateTimeOffset StartedAt, DateTimeOffset CompletedAt)? TryCreateBounds(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        if (startedAt > completedAt)
        {
            return null;
        }

        return (startedAt, completedAt);
    }

    /// <summary>
    /// Консервативная оболочка неопределённости: покрывает оба момента без утверждения, что
    /// чтение шло от <paramref name="startedAt"/> к <paramref name="completedAt"/>. Используется,
    /// когда принятое REST-наблюдение нельзя оставить без защиты из-за отката часов.
    /// </summary>
    public static (DateTimeOffset StartedAt, DateTimeOffset CompletedAt) ConservativeEnvelope(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt) =>
        startedAt <= completedAt ? (startedAt, completedAt) : (completedAt, startedAt);

    /// <summary>
    /// Позиционное наблюдение пересекается с REST-доказательством, если backend ObservedAt или
    /// биржевой факт попадает в полное окно или в окно своего символа, либо раньше начала
    /// scoped-окна того же символа. Ограниченный ObservedAt сам по себе не скрывает пересечение
    /// биржевого времени с REST-интервалом.
    /// </summary>
    public static bool PositionOverlapsRest(
        DateTimeOffset observedAt,
        string symbol,
        IEnumerable<ExchangeRestOrderingInterval> intervals,
        DateTimeOffset? exchangeFactAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(intervals);

        return PositionTimestampOverlapsRest(observedAt, symbol, intervals) ||
               (exchangeFactAt is { } exchangeAt &&
                PositionTimestampOverlapsRest(exchangeAt, symbol, intervals));
    }

    private static bool PositionTimestampOverlapsRest(
        DateTimeOffset timestamp,
        string symbol,
        IEnumerable<ExchangeRestOrderingInterval> intervals)
    {
        var normalizedAt = TimestampCanonicalizer.ToUtcMicroseconds(timestamp);
        foreach (var interval in intervals)
        {
            var startedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.StartedAt);
            var completedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.CompletedAt);
            if (startedAt > completedAt)
            {
                continue;
            }

            switch (interval.Kind)
            {
                case ExchangeRestOrderingKind.FullPositionsComplete:
                    if (normalizedAt >= startedAt && normalizedAt <= completedAt)
                    {
                        return true;
                    }

                    break;

                case ExchangeRestOrderingKind.ScopedSymbol when
                    string.Equals(interval.Symbol, symbol, StringComparison.OrdinalIgnoreCase):
                    // Contains([started, completed]) или earlier than scoped start → at <= completed.
                    if (normalizedAt <= completedAt)
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Wallet-событие неоднозначно относительно REST-баланса, если backend ObservedAt или биржевой
    /// <paramref name="messageCreatedAt"/> лежит в строгом интерьере интервала чтения баланса.
    /// </summary>
    public static bool WalletOverlapsBalanceRead(
        DateTimeOffset? observedAt,
        DateTimeOffset? messageCreatedAt,
        IEnumerable<ExchangeRestOrderingInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        foreach (var interval in intervals)
        {
            if (interval.Kind != ExchangeRestOrderingKind.Balance)
            {
                continue;
            }

            var startedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.StartedAt);
            var completedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.CompletedAt);
            if (startedAt >= completedAt)
            {
                continue;
            }

            if (observedAt is { } at &&
                IsStrictInterior(TimestampCanonicalizer.ToUtcMicroseconds(at), startedAt, completedAt))
            {
                return true;
            }

            if (messageCreatedAt is { } exchangeAt &&
                IsStrictInterior(TimestampCanonicalizer.ToUtcMicroseconds(exchangeAt), startedAt, completedAt))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStrictInterior(
        DateTimeOffset value,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt) =>
        value > startedAt && value < completedAt;
}
