using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.Ordering;

/// <summary>
/// Account-owned интервал успешного REST-чтения. Непересекающиеся интервалы не образуют
/// непрерывного покрытия промежутка между ними; отсутствие интервала не доказывает безопасность
/// прямого применения потокового наблюдения.
/// </summary>
public sealed record ExchangeRestOrderingInterval
{
    public required Guid Id
    {
        get;
        init => field = value != Guid.Empty
            ? value
            : throw new ArgumentException("Id must be initialized.", nameof(Id));
    }

    public required ExchangeAccountId ExchangeAccountId
    {
        get;
        init => field = value != default
            ? value
            : throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(ExchangeAccountId));
    }

    public required ExchangeRestOrderingKind Kind
    {
        get;
        init => field = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Kind), value, "Unknown ordering kind.");
    }

    /// <summary>
    /// Символ инструмента для <see cref="ExchangeRestOrderingKind.ScopedSymbol"/>; для остальных
    /// видов должен быть <see langword="null"/>.
    /// </summary>
    public string? Symbol { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Момент записи интервала backend; не используется как доказательство биржевого порядка.</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
