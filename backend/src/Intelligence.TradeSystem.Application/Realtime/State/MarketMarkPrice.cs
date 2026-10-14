using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.State;

/// <summary>
/// Последняя подтверждённая публичная mark price инструмента, общая для всех пользователей.
/// </summary>
/// <remarks>
/// Не содержит пользователя, аккаунта, позиций или credentials. Свежесть оценивается на момент
/// чтения по <see cref="ObservedAt"/>; сохранённое значение само по себе свежим не считается.
/// </remarks>
public sealed record MarketMarkPrice
{
    public const int SymbolMaxLength = 128;

    public required ExchangeId Exchange
    {
        get;
        init => field = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Exchange), value, "Unknown exchange.");
    }

    public required MarketCategory Category
    {
        get;
        init => field = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Category), value, "Unknown market category.");
    }

    public required InstrumentId Symbol
    {
        get;
        init => field = value.Value is { Length: > 0 and <= SymbolMaxLength }
            ? value
            : throw new ArgumentException("Symbol must be initialized and not too long.", nameof(Symbol));
    }

    public required decimal MarkPrice
    {
        get;
        init => field = value > 0m
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MarkPrice), value, "Mark price must be positive.");
    }

    /// <summary>
    /// Время данных по часам биржи. Обязательно: без достоверной отметки нельзя ни упорядочить
    /// наблюдения, ни оценить свежесть, а время получения его не подменяет.
    /// </summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Время получения наблюдения backend.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    public required MarketMarkPriceSource Source
    {
        get;
        init => field = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Source), value, "Unknown mark price source.");
    }
}
