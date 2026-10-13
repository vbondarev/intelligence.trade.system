using System.Text.Json;
using System.Text.RegularExpressions;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;
using BybitAccountType = Bybit.Net.Enums.AccountType;
using PositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

/// <summary>
/// Нормализует сообщения потоков Bybit в прикладные наблюдения.
/// </summary>
/// <remarks>
/// SDK заполняет отсутствующие поля значениями по умолчанию (например, отсутствующая
/// <c>category</c> становится <c>linear</c>, а <c>size</c> — нулём) и не передаёт
/// <c>creationTime</c> приватных сообщений. Поэтому исходное сообщение читается ограниченным
/// разбором только внутри этого класса; оно не покидает адаптер и не логируется.
/// </remarks>
internal static partial class BybitRealtimeObservationMapper
{
    internal const int MaxOriginalDataLength = 1_048_576;
    internal static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);
    internal static readonly DateTimeOffset MinPlausibleTimestamp = new(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string LinearCategory = "linear";
    private static readonly JsonDocumentOptions EnvelopeParsingOptions = new() { MaxDepth = 16 };

    public static IReadOnlyList<PrivatePositionObservation> MapPositions(
        DataEvent<BybitPositionUpdate[]> update,
        ExchangeAccountId exchangeAccountId,
        Func<string, SettlementAsset?> resolveSettlement)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(resolveSettlement);

        var positions = update.Data ?? [];
        var receivedAt = ToUtc(update.ReceiveTime);
        var envelope = ReadEnvelope(update.OriginalData, receivedAt, readItems: true);

        // Поля сопоставляются по индексу элемента data; при расхождении количества наличие полей не проверено.
        var items = envelope.Items is { } rawItems && rawItems.Count == positions.Length ? rawItems : null;

        var observations = new PrivatePositionObservation[positions.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            observations[index] = MapPosition(
                positions[index],
                items?[index],
                exchangeAccountId,
                envelope.CreationTime,
                receivedAt,
                resolveSettlement);
        }

        return observations;
    }

    public static PrivateWalletObservation MapWallet(
        DataEvent<BybitBalance[]> update,
        ExchangeAccountId exchangeAccountId)
    {
        ArgumentNullException.ThrowIfNull(update);

        var receivedAt = ToUtc(update.ReceiveTime);
        var envelope = ReadEnvelope(update.OriginalData, receivedAt, readItems: false);

        var balances = (update.Data ?? [])
            .Select(MapApplicableBalance)
            .OfType<AccountBalance>()
            .ToArray();

        return new PrivateWalletObservation
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = exchangeAccountId,
            Kind = balances.Length > 0
                ? PrivateWalletObservationKind.PartialUpdate
                : PrivateWalletObservationKind.Unconfirmed,
            Balances = balances,
            MessageCreatedAt = envelope.CreationTime,
            ReceivedAt = receivedAt,
        };
    }

    /// <returns><see langword="null"/>, если сообщение не содержит допустимого символа.</returns>
    public static MarketMarkPriceObservation? MapMarkPrice(DataEvent<BybitLinearTickerUpdate> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.Data is not { } ticker || !IsValidSymbol(ticker.Symbol))
        {
            return null;
        }

        var receivedAt = ToUtc(update.ReceiveTime);
        return new MarketMarkPriceObservation
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = ticker.Symbol,
            MarkPrice = ticker.MarkPrice > 0m ? ticker.MarkPrice : null,
            UpdateKind = update.UpdateType == SocketUpdateType.Snapshot
                ? MarketPriceUpdateKind.Snapshot
                : MarketPriceUpdateKind.Delta,
            ObservedAt = ToPlausibleUtc(update.DataTime, receivedAt),
            ReceivedAt = receivedAt,
        };
    }

    private static PrivatePositionObservation MapPosition(
        BybitPosition position,
        RawPositionFields? rawFields,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset? messageCreatedAt,
        DateTimeOffset receivedAt,
        Func<string, SettlementAsset?> resolveSettlement)
    {
        var symbol = position.Symbol ?? string.Empty;
        var side = position.Side.MapPositionSide();
        var providerUpdatedAt = ToPlausibleUtc(position.UpdateTime, receivedAt);
        var providerCreatedAt = ToPlausibleUtc(position.CreateTime, receivedAt);
        var observation = new PrivatePositionObservation
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = exchangeAccountId,
            Kind = PrivatePositionObservationKind.Ambiguous,
            Symbol = symbol,
            Category = rawFields?.Category is null ? null : MapCategory(position.Category),
            PositionIdx = (int)position.PositionIdx,
            Side = side,
            Size = position.Quantity,
            ProviderSequence = position.Sequence > 0 ? position.Sequence : null,
            ProviderUpdatedAt = providerUpdatedAt,
            ProviderCreatedAt = providerCreatedAt,
            MessageCreatedAt = messageCreatedAt,
            ReceivedAt = receivedAt,
        };

        if (rawFields is not { } fields)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.UnverifiableMessage);
        }

        if (!IsValidSymbol(symbol))
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.InvalidSymbol);
        }

        if (fields.Category is null)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.MissingField);
        }

        if (!string.Equals(fields.Category, LinearCategory, StringComparison.Ordinal)
            || position.Category != Category.Linear)
        {
            return Classify(observation, PrivatePositionObservationKind.Unsupported, PrivatePositionObservationIssue.UnsupportedCategory);
        }

        if (!fields.HasPositionIdx || !fields.HasSize)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.MissingField);
        }

        if (!Enum.IsDefined(position.PositionIdx))
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.InvalidPositionIdx);
        }

        if (position.Quantity < 0m)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.InvalidSize);
        }

        if (observation.ProviderSequence is null && providerUpdatedAt is null)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.MissingRevision);
        }

        var settlementAsset = resolveSettlement(symbol);
        observation = observation with { SettlementAsset = settlementAsset };
        if (settlementAsset is { } asset && !IsSupportedSettlementAsset(asset))
        {
            return Classify(observation, PrivatePositionObservationKind.Unsupported, PrivatePositionObservationIssue.UnsupportedSettlementAsset);
        }

        if (position.Quantity == 0m)
        {
            return observation with { Kind = PrivatePositionObservationKind.PotentialClose };
        }

        if (side == PositionSide.Unknown)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.UnknownSide);
        }

        if (!IsSideConsistentWithPositionIdx(position.PositionIdx, side))
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.PositionIdxSideMismatch);
        }

        if (settlementAsset is null)
        {
            return Classify(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.SettlementAssetUnconfirmed);
        }

        var openPosition = position.MapOpenPosition(MarketCategory.Linear, settlementAsset) with
        {
            CreatedTime = providerCreatedAt,
            UpdatedTime = providerUpdatedAt,
        };

        return observation with
        {
            Kind = PrivatePositionObservationKind.Open,
            Position = openPosition,
        };
    }

    private static PrivatePositionObservation Classify(
        PrivatePositionObservation observation,
        PrivatePositionObservationKind kind,
        PrivatePositionObservationIssue issue) =>
        observation with { Kind = kind, Issue = issue };

    private static MarketCategory? MapCategory(Category category) =>
        category switch
        {
            Category.Linear => MarketCategory.Linear,
            Category.Inverse => MarketCategory.Inverse,
            Category.Spot => MarketCategory.Spot,
            _ => null,
        };

    /// <summary>
    /// Нормализует запись поддерживаемого типа аккаунта, если она подтверждает хотя бы одно
    /// финансовое значение уровня аккаунта или монеты. Остальные типы <c>MapAccountType</c>
    /// приводит к Unified, что исказило бы источник баланса.
    /// </summary>
    /// <remarks>
    /// Монеты без финансовых значений исключаются: иначе запись вида <c>{"coin":"USDT"}</c> выглядела бы
    /// подтверждённым балансом монеты со всеми неизвестными суммами. Общий REST-маппинг не меняется.
    /// </remarks>
    private static AccountBalance? MapApplicableBalance(BybitBalance balance)
    {
        if (balance.AccountType is not (BybitAccountType.Unified or BybitAccountType.Contract or BybitAccountType.Spot))
        {
            return null;
        }

        var mapped = balance.MapAccountBalance();
        var coins = mapped.Coins.Where(IsConfirmedCoinBalance).ToArray();
        var hasAccountValues = mapped.TotalEquity is not null
            || mapped.TotalWalletBalance is not null
            || mapped.TotalAvailableBalance is not null
            || mapped.TotalPerpUnrealizedPnl is not null;
        if (!hasAccountValues && coins.Length == 0)
        {
            return null;
        }

        return mapped with { Coins = coins };
    }

    /// <summary>
    /// Монета с идентификатором и хотя бы одним присутствующим финансовым значением; нулевая сумма
    /// является подтверждённым значением, отсутствующая или пустая — нет.
    /// </summary>
    private static bool IsConfirmedCoinBalance(CoinBalance coin) =>
        !string.IsNullOrWhiteSpace(coin.Coin)
        && (coin.Equity is not null
            || coin.UsdValue is not null
            || coin.WalletBalance is not null
            || coin.AvailableBalance is not null
            || coin.Locked is not null
            || coin.UnrealizedPnl is not null);

    private static bool IsSupportedSettlementAsset(SettlementAsset asset) =>
        asset.Value is "USDT" or "USDC";

    private static bool IsSideConsistentWithPositionIdx(PositionIdx positionIdx, PositionSide side) =>
        positionIdx switch
        {
            PositionIdx.OneWayMode => true,
            PositionIdx.BuyHedgeMode => side == PositionSide.Long,
            PositionIdx.SellHedgeMode => side == PositionSide.Short,
            _ => false,
        };

    private static bool IsValidSymbol(string? symbol) =>
        symbol is not null && SymbolPattern().IsMatch(symbol);

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        });

    /// <summary>
    /// Отбрасывает отметки до запуска Bybit V5 и заметно опережающие время получения.
    /// </summary>
    private static DateTimeOffset? ToPlausibleUtc(DateTime? value, DateTimeOffset receivedAt) =>
        value is { } timestamp ? ToPlausible(ToUtc(timestamp), receivedAt) : null;

    private static DateTimeOffset? ToPlausible(DateTimeOffset timestamp, DateTimeOffset receivedAt) =>
        timestamp >= MinPlausibleTimestamp && timestamp <= receivedAt + MaxClockSkew
            ? timestamp
            : null;

    internal static PrivateEnvelope ReadEnvelope(string? originalData, DateTimeOffset receivedAt, bool readItems)
    {
        if (string.IsNullOrEmpty(originalData) || originalData.Length > MaxOriginalDataLength)
        {
            return PrivateEnvelope.Unavailable;
        }

        try
        {
            using var document = JsonDocument.Parse(originalData, EnvelopeParsingOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return PrivateEnvelope.Unavailable;
            }

            return new PrivateEnvelope(
                ReadCreationTime(root, receivedAt),
                readItems ? ReadPositionFields(root) : null);
        }
        catch (JsonException)
        {
            return PrivateEnvelope.Unavailable;
        }
    }

    private static DateTimeOffset? ReadCreationTime(JsonElement root, DateTimeOffset receivedAt)
    {
        if (!root.TryGetProperty("creationTime", out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var milliseconds)
            || milliseconds < MinPlausibleTimestamp.ToUnixTimeMilliseconds()
            || milliseconds > (receivedAt + MaxClockSkew).ToUnixTimeMilliseconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static List<RawPositionFields>? ReadPositionFields(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<RawPositionFields>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                items.Add(default);
                continue;
            }

            var category = item.TryGetProperty("category", out var categoryElement)
                && categoryElement.ValueKind == JsonValueKind.String
                    ? categoryElement.GetString()
                    : null;
            items.Add(new RawPositionFields(
                string.IsNullOrEmpty(category) ? null : category,
                HasValue(item, "positionIdx"),
                HasValue(item, "size")));
        }

        return items;
    }

    private static bool HasValue(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var element)
        && (element.ValueKind == JsonValueKind.Number
            || (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 }));

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();

    internal sealed record PrivateEnvelope(
        DateTimeOffset? CreationTime,
        IReadOnlyList<RawPositionFields>? Items)
    {
        public static PrivateEnvelope Unavailable { get; } = new(null, null);
    }

    internal readonly record struct RawPositionFields(
        string? Category,
        bool HasPositionIdx,
        bool HasSize);
}
