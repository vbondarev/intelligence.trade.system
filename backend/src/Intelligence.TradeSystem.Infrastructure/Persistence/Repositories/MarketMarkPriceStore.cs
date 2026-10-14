using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

internal sealed class MarketMarkPriceStore(TradeSystemDbContext dbContext) : IMarketMarkPriceStore
{
    public async Task<IReadOnlyCollection<MarketMarkPrice>> GetAsync(
        ExchangeId exchange,
        MarketCategory category,
        IReadOnlyCollection<InstrumentId> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (!Enum.IsDefined(exchange))
        {
            throw new ArgumentOutOfRangeException(nameof(exchange), exchange, "Unknown exchange.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown market category.");
        }

        var values = symbols.Select(symbol => symbol.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0)
        {
            return [];
        }

        var entities = await dbContext.MarketMarkPrices
            .AsNoTracking()
            .Where(price =>
                price.ExchangeId == exchange &&
                price.MarketCategory == category &&
                values.Contains(price.InstrumentId))
            .OrderBy(price => price.InstrumentId)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToModel).ToArray();
    }

    public async Task<int> UpsertAsync(
        IReadOnlyCollection<MarketMarkPrice> prices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prices);
        if (prices.Count == 0)
        {
            return 0;
        }

        // ON CONFLICT не допускает повторного изменения строки в одной команде, поэтому на ключ
        // остаётся одно, самое новое наблюдение.
        var latest = prices
            .Select(price => price ?? throw new ArgumentException("Prices must not contain null.", nameof(prices)))
            .GroupBy(price => (price.Exchange, price.Category, Symbol: price.Symbol.Value))
            .Select(group => group.MaxBy(price => PersistenceDateTime.ToUtc(price.ObservedAt))!)
            .ToArray();

        // INSERT обрабатывает строки в порядке ORDER BY, поэтому каждая команда блокирует строки
        // и ожидает конкурентные вставки ключей в одном порядке независимо от порядка пакета.
        // COLLATE "C" делает порядок побайтовым и не зависящим от collation базы. Порядок
        // согласован только внутри одной команды: несколько upsert или другие блокировки в одной
        // транзакции требуют собственного согласования порядка.
        return await TradeSystemDbCommand.ExecuteNonQueryAsync(
            dbContext,
            """
            INSERT INTO market_mark_prices AS stored (
                exchange_id,
                market_category,
                instrument_id,
                mark_price,
                observed_at,
                received_at,
                source)
            SELECT
                incoming.exchange_id,
                incoming.market_category,
                incoming.instrument_id,
                incoming.mark_price,
                incoming.observed_at,
                incoming.received_at,
                incoming.source
            FROM unnest(
                @exchange_ids,
                @market_categories,
                @instrument_ids,
                @mark_prices,
                @observed_at,
                @received_at,
                @sources)
                AS incoming(exchange_id, market_category, instrument_id, mark_price, observed_at, received_at, source)
            ORDER BY
                incoming.exchange_id COLLATE "C",
                incoming.market_category COLLATE "C",
                incoming.instrument_id COLLATE "C"
            ON CONFLICT (exchange_id, market_category, instrument_id) DO UPDATE
            SET mark_price = EXCLUDED.mark_price,
                observed_at = EXCLUDED.observed_at,
                received_at = EXCLUDED.received_at,
                source = EXCLUDED.source
            WHERE stored.observed_at < EXCLUDED.observed_at
            """,
            [
                ArrayParameter("exchange_ids", NpgsqlDbType.Varchar, latest.Select(price => price.Exchange.ToString())),
                ArrayParameter(
                    "market_categories",
                    NpgsqlDbType.Varchar,
                    latest.Select(price => price.Category.ToString())),
                ArrayParameter("instrument_ids", NpgsqlDbType.Varchar, latest.Select(price => price.Symbol.Value)),
                ArrayParameter("mark_prices", NpgsqlDbType.Numeric, latest.Select(price => price.MarkPrice)),
                ArrayParameter(
                    "observed_at",
                    NpgsqlDbType.TimestampTz,
                    latest.Select(price => PersistenceDateTime.ToUtc(price.ObservedAt))),
                ArrayParameter(
                    "received_at",
                    NpgsqlDbType.TimestampTz,
                    latest.Select(price => PersistenceDateTime.ToUtc(price.ReceivedAt))),
                ArrayParameter("sources", NpgsqlDbType.Varchar, latest.Select(price => price.Source.ToString())),
            ],
            cancellationToken);
    }

    private static MarketMarkPrice ToModel(MarketMarkPriceEntity entity) =>
        new()
        {
            Exchange = entity.ExchangeId,
            Category = entity.MarketCategory,
            Symbol = InstrumentId.From(entity.InstrumentId),
            MarkPrice = entity.MarkPrice,
            ObservedAt = entity.ObservedAt,
            ReceivedAt = entity.ReceivedAt,
            Source = entity.Source,
        };

    private static NpgsqlParameter ArrayParameter<T>(string name, NpgsqlDbType elementType, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() };
}
