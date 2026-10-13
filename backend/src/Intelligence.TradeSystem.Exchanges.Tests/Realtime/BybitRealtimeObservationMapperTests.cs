using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects.Sockets;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using static Intelligence.TradeSystem.Exchanges.Tests.Realtime.BybitRealtimeTestData;
using BybitPositionSide = Bybit.Net.Enums.PositionSide;
using DomainAccountType = Intelligence.TradeSystem.Domain.AccountType;
using PositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitRealtimeObservationMapperTests
{
    private static readonly ExchangeAccountId AccountId = ExchangeAccountId.New();
    private static readonly SettlementAsset Usdt = SettlementAsset.From("USDT");
    private static readonly SettlementAsset Usdc = SettlementAsset.From("USDC");

    [Fact]
    public async Task MapPositions_Maps_OneWay_Long_Usdt_Position_As_Open()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem()));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.Open);
        observation.Issue.Should().BeNull();
        observation.Exchange.Should().Be(ExchangeId.Bybit);
        observation.ExchangeAccountId.Should().Be(AccountId);
        observation.Symbol.Should().Be("BTCUSDT");
        observation.Category.Should().Be(MarketCategory.Linear);
        observation.PositionIdx.Should().Be(0);
        observation.Side.Should().Be(PositionSide.Long);
        observation.Size.Should().Be(0.5m);
        observation.ProviderSequence.Should().Be(Sequence);
        observation.ProviderUpdatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(UpdatedTimeMs));
        observation.ProviderCreatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(CreatedTimeMs));
        observation.MessageCreatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(CreationTimeMs));
        observation.IsLatencyMeasurable.Should().BeTrue();
        observation.ReceivedAt.Offset.Should().Be(TimeSpan.Zero);
        observation.SettlementAsset.Should().Be(Usdt);

        var position = observation.Position;
        position.Should().NotBeNull();
        position!.Symbol.Should().Be("BTCUSDT");
        position.Category.Should().Be(MarketCategory.Linear);
        position.Side.Should().Be(PositionSide.Long);
        position.Size.Should().Be(0.5m);
        position.PositionIdx.Should().Be(0);
        position.SettlementAsset.Should().Be(Usdt);
        position.UpdatedTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(UpdatedTimeMs));
        position.CreatedTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(CreatedTimeMs));
    }

    [Theory]
    [InlineData(0, "Sell", "BTCUSDT", PositionSide.Short)]
    [InlineData(1, "Buy", "BTCUSDT", PositionSide.Long)]
    [InlineData(2, "Sell", "BTCUSDT", PositionSide.Short)]
    [InlineData(0, "Buy", "BTCPERP", PositionSide.Long)]
    public async Task MapPositions_Maps_OneWay_And_Hedge_Positions_With_Confirmed_Settlement(
        int positionIdx,
        string side,
        string symbol,
        PositionSide expectedSide)
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("positionIdx", positionIdx), ("side", side), ("symbol", symbol))));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.Open);
        observation.PositionIdx.Should().Be(positionIdx);
        observation.Side.Should().Be(expectedSide);
        observation.SettlementAsset.Should().Be(symbol == "BTCPERP" ? Usdc : Usdt);
        observation.Position!.PositionIdx.Should().Be(positionIdx);
        observation.Position.Side.Should().Be(expectedSide);
        observation.Position.SettlementAsset.Should().Be(observation.SettlementAsset);
    }

    [Theory]
    [InlineData(1, "Sell")]
    [InlineData(2, "Buy")]
    public async Task MapPositions_Marks_Hedge_Side_Mismatch_As_Ambiguous(int positionIdx, string side)
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("positionIdx", positionIdx), ("side", side))));

        AssertRejected(MapSingle(update), PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.PositionIdxSideMismatch);
    }

    [Theory]
    [InlineData("Buy")]
    [InlineData("")]
    public async Task MapPositions_Treats_Explicit_Zero_Size_As_Potential_Close(string side)
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("size", "0"), ("side", side))));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.PotentialClose);
        observation.Issue.Should().BeNull();
        observation.Size.Should().Be(0m);
        observation.Position.Should().BeNull();
    }

    [Fact]
    public async Task MapPositions_Potential_Close_Does_Not_Require_Confirmed_Settlement()
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("size", "0"), ("side", ""), ("symbol", "NEWUSDT"))));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.PotentialClose);
        observation.SettlementAsset.Should().BeNull();
    }

    [Theory]
    [InlineData("size")]
    [InlineData("positionIdx")]
    public async Task MapPositions_Does_Not_Trust_Sdk_Defaults_For_Missing_Required_Fields(string missingField)
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem((missingField, null))));

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.MissingField);
        observation.Kind.Should().NotBe(PrivatePositionObservationKind.PotentialClose);
    }

    [Fact]
    public async Task MapPositions_Missing_Category_Is_Ambiguous_Instead_Of_Sdk_Default_Linear()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("category", null))));

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.MissingField);
        observation.Category.Should().BeNull();
    }

    [Theory]
    [InlineData("inverse", MarketCategory.Inverse)]
    [InlineData("spot", MarketCategory.Spot)]
    [InlineData("option", null)]
    [InlineData("weird", null)]
    public async Task MapPositions_Marks_Non_Linear_Category_As_Unsupported(string category, MarketCategory? expectedCategory)
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("category", category))));

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Unsupported, PrivatePositionObservationIssue.UnsupportedCategory);
        observation.Category.Should().Be(expectedCategory);
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("")]
    [InlineData("None")]
    public async Task MapPositions_Marks_Unknown_Side_Of_Non_Zero_Position_As_Ambiguous(string side)
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("side", side))));

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.UnknownSide);
        observation.Side.Should().Be(PositionSide.Unknown);
    }

    [Theory]
    [InlineData("btcusdt")]
    [InlineData("BTC/USDT")]
    [InlineData("")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public async Task MapPositions_Marks_Invalid_Symbol_As_Ambiguous(string symbol)
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("symbol", symbol))));

        AssertRejected(MapSingle(update), PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.InvalidSymbol);
    }

    [Fact]
    public async Task MapPositions_Marks_Undefined_PositionIdx_As_Ambiguous()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("positionIdx", 3))));

        AssertRejected(MapSingle(update), PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.InvalidPositionIdx);
    }

    [Fact]
    public async Task MapPositions_Marks_Negative_Size_As_Ambiguous()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("size", "-1"))));

        AssertRejected(MapSingle(update), PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.InvalidSize);
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData(-1L, null)]
    [InlineData(0L, "")]
    public async Task MapPositions_Requires_Provider_Revision(long? sequence, string? updatedTime)
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("seq", sequence), ("updatedTime", updatedTime))));

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.MissingRevision);
        observation.ProviderSequence.Should().BeNull();
        observation.ProviderUpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task MapPositions_Accepts_UpdatedTime_As_Revision_Without_Sequence()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("seq", null))));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.Open);
        observation.ProviderSequence.Should().BeNull();
        observation.ProviderUpdatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(UpdatedTimeMs));
    }

    [Fact]
    public async Task MapPositions_Accepts_Sequence_As_Revision_Without_UpdatedTime()
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("updatedTime", "0"), ("createdTime", ""))));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.Open);
        observation.ProviderSequence.Should().Be(Sequence);
        observation.ProviderUpdatedAt.Should().BeNull();
        observation.ProviderCreatedAt.Should().BeNull();
        observation.Position!.UpdatedTime.Should().BeNull();
        observation.Position.CreatedTime.Should().BeNull();
    }

    [Fact]
    public async Task MapPositions_Drops_Implausible_Provider_Timestamps()
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(("updatedTime", "4102444800000"), ("createdTime", "1000"))));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.Open);
        observation.ProviderUpdatedAt.Should().BeNull();
        observation.ProviderCreatedAt.Should().BeNull();
        observation.Position!.UpdatedTime.Should().BeNull();
        observation.Position.CreatedTime.Should().BeNull();
    }

    [Fact]
    public async Task MapPositions_Marks_Unsupported_Settlement_Asset()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("symbol", "BTCBTC"))));

        var observation = MapSingle(update, symbol => symbol == "BTCBTC" ? SettlementAsset.From("BTC") : null);

        AssertRejected(observation, PrivatePositionObservationKind.Unsupported, PrivatePositionObservationIssue.UnsupportedSettlementAsset);
        observation.SettlementAsset.Should().Be(SettlementAsset.From("BTC"));
    }

    [Fact]
    public async Task MapPositions_Does_Not_Guess_Settlement_From_Symbol_Suffix()
    {
        var update = await CapturePositionsAsync(PositionMessage(PositionItem(("symbol", "NEWUSDT"))));

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.SettlementAssetUnconfirmed);
        observation.SettlementAsset.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4102444800000L)]
    [InlineData(1000L)]
    [InlineData("1697682317044")]
    public async Task MapPositions_Marks_Missing_Or_Implausible_CreationTime_As_Not_Latency_Measurable(object? creationTime)
    {
        var update = await CapturePositionsAsync(PrivateMessage("position", creationTime, PositionItem()));

        var observation = MapSingle(update);

        observation.Kind.Should().Be(PrivatePositionObservationKind.Open);
        observation.MessageCreatedAt.Should().BeNull();
        observation.IsLatencyMeasurable.Should().BeFalse();
        observation.ReceivedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task MapPositions_Maps_Every_Item_In_Message_Order()
    {
        var update = await CapturePositionsAsync(PositionMessage(
            PositionItem(),
            PositionItem(("symbol", "BTCPERP"), ("side", "Sell")),
            PositionItem(("category", "inverse"))));

        var observations = Map(update);

        observations.Select(observation => observation.Kind).Should().Equal(
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Open,
            PrivatePositionObservationKind.Unsupported);
        observations[1].Symbol.Should().Be("BTCPERP");
        observations[1].Side.Should().Be(PositionSide.Short);
        observations.Should().OnlyContain(observation => observation.MessageCreatedAt == DateTimeOffset.FromUnixTimeMilliseconds(CreationTimeMs));
    }

    [Fact]
    public void MapPositions_Without_Original_Message_Is_Unverifiable()
    {
        var update = PositionEvent(originalData: null, ValidPosition());

        var observation = MapSingle(update);

        AssertRejected(observation, PrivatePositionObservationKind.Ambiguous, PrivatePositionObservationIssue.UnverifiableMessage);
        observation.MessageCreatedAt.Should().BeNull();
    }

    [Fact]
    public void MapPositions_With_Item_Count_Mismatch_Is_Unverifiable()
    {
        var update = PositionEvent(PositionMessage(PositionItem()), ValidPosition(), ValidPosition());

        var observations = Map(update);

        observations.Should().HaveCount(2).And.OnlyContain(observation =>
            observation.Kind == PrivatePositionObservationKind.Ambiguous
            && observation.Issue == PrivatePositionObservationIssue.UnverifiableMessage);
    }

    [Fact]
    public void MapPositions_Returns_Empty_For_Empty_Data()
    {
        var update = PositionEvent(PositionMessage());

        Map(update).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    public void ReadEnvelope_Returns_Unavailable_For_Invalid_Message(string originalData)
    {
        var envelope = BybitRealtimeObservationMapper.ReadEnvelope(originalData, DateTimeOffset.UtcNow, readItems: true);

        envelope.Should().BeSameAs(BybitRealtimeObservationMapper.PrivateEnvelope.Unavailable);
    }

    [Fact]
    public void ReadEnvelope_Rejects_Too_Deep_Message()
    {
        var originalData = $"{{\"creationTime\":{CreationTimeMs},\"x\":{new string('[', 32)}{new string(']', 32)}}}";

        var envelope = BybitRealtimeObservationMapper.ReadEnvelope(originalData, DateTimeOffset.UtcNow, readItems: true);

        envelope.Should().BeSameAs(BybitRealtimeObservationMapper.PrivateEnvelope.Unavailable);
    }

    [Fact]
    public void ReadEnvelope_Rejects_Oversized_Message_Without_Parsing()
    {
        var padding = new string(' ', BybitRealtimeObservationMapper.MaxOriginalDataLength);
        var originalData = $"{{\"creationTime\":{CreationTimeMs}}}{padding}";

        var envelope = BybitRealtimeObservationMapper.ReadEnvelope(originalData, DateTimeOffset.UtcNow, readItems: true);

        envelope.Should().BeSameAs(BybitRealtimeObservationMapper.PrivateEnvelope.Unavailable);
    }

    [Fact]
    public void ReadEnvelope_Reads_CreationTime_And_Field_Presence()
    {
        var originalData = PositionMessage(PositionItem(), PositionItem(("size", ""), ("category", null)), "42");

        var envelope = BybitRealtimeObservationMapper.ReadEnvelope(originalData, DateTimeOffset.UtcNow, readItems: true);

        envelope.CreationTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(CreationTimeMs));
        envelope.Items.Should().Equal(
            new BybitRealtimeObservationMapper.RawPositionFields("linear", true, true),
            new BybitRealtimeObservationMapper.RawPositionFields(null, true, false),
            default(BybitRealtimeObservationMapper.RawPositionFields));
    }

    [Fact]
    public async Task MapWallet_Keeps_Only_Supported_Account_Types_And_CreationTime()
    {
        var update = await CaptureWalletAsync(WalletMessage());

        var observation = BybitRealtimeObservationMapper.MapWallet(update, AccountId);

        observation.Exchange.Should().Be(ExchangeId.Bybit);
        observation.ExchangeAccountId.Should().Be(AccountId);
        observation.MessageCreatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(CreationTimeMs));
        observation.IsLatencyMeasurable.Should().BeTrue();
        observation.Balances.Select(balance => balance.AccountType).Should().Equal(DomainAccountType.Unified, DomainAccountType.Contract);
        var unified = observation.Balances[0];
        unified.TotalEquity.Should().Be(12_500m);
        unified.TotalAvailableBalance.Should().Be(8_000m);
        unified.Coins.Should().ContainSingle(coin => coin.Coin == "USDT" && coin.WalletBalance == 12_000m);
        // Неподдерживаемый FUND отброшен: наблюдение остаётся частичным и не подтверждает состав аккаунта.
        observation.Kind.Should().Be(PrivateWalletObservationKind.PartialUpdate);
        observation.RequiresRestReconciliation.Should().BeFalse();
    }

    [Fact]
    public async Task MapWallet_Empty_Data_Is_Unconfirmed_And_Requires_Rest_Reconciliation()
    {
        var update = await CaptureWalletAsync(PrivateMessage("wallet", CreationTimeMs));

        var observation = BybitRealtimeObservationMapper.MapWallet(update, AccountId);

        observation.Kind.Should().Be(PrivateWalletObservationKind.Unconfirmed);
        observation.RequiresRestReconciliation.Should().BeTrue();
        observation.Balances.Should().BeEmpty();
        observation.MessageCreatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("""{"accountType":"FUND","totalEquity":"1","coin":[{"coin":"USDT","walletBalance":"1"}]}""")]
    [InlineData("""{"accountType":"INVESTMENT","totalEquity":"5"}""")]
    [InlineData("""{"accountType":"UNIFIED","coin":[]}""")]
    public async Task MapWallet_Without_Applicable_Balances_Is_Unconfirmed(string item)
    {
        var update = await CaptureWalletAsync(PrivateMessage("wallet", CreationTimeMs, item));

        var observation = BybitRealtimeObservationMapper.MapWallet(update, AccountId);

        observation.Kind.Should().Be(PrivateWalletObservationKind.Unconfirmed);
        observation.RequiresRestReconciliation.Should().BeTrue();
        observation.Balances.Should().BeEmpty();
    }

    [Fact]
    public async Task MapWallet_Unified_Balance_Is_Partial_Update_With_Preserved_Values()
    {
        var update = await CaptureWalletAsync(PrivateMessage(
            "wallet",
            CreationTimeMs,
            """{"accountType":"UNIFIED","totalEquity":"12500","totalWalletBalance":"12000","totalAvailableBalance":"8000","totalPerpUPL":"500","coin":[{"coin":"USDT","equity":"12500","usdValue":"12499.5","walletBalance":"12000","locked":"10","unrealisedPnl":"500"}]}"""));

        var observation = BybitRealtimeObservationMapper.MapWallet(update, AccountId);

        observation.Kind.Should().Be(PrivateWalletObservationKind.PartialUpdate);
        observation.RequiresRestReconciliation.Should().BeFalse();
        observation.Balances.Should().ContainSingle().Which.Should().BeEquivalentTo(new AccountBalance(
            DomainAccountType.Unified,
            TotalEquity: 12_500m,
            TotalWalletBalance: 12_000m,
            TotalAvailableBalance: 8_000m,
            TotalPerpUnrealizedPnl: 500m,
            [new CoinBalance("USDT", 12_500m, 12_499.5m, 12_000m, AvailableBalance: null, 10m, 500m)]));
    }

    [Fact]
    public async Task MapWallet_Keeps_Missing_Financial_Values_As_Null()
    {
        var update = await CaptureWalletAsync(PrivateMessage(
            "wallet",
            CreationTimeMs,
            """{"accountType":"UNIFIED","totalWalletBalance":"12000","totalAvailableBalance":"","coin":[{"coin":"BTC","equity":"0.5","walletBalance":""}]}"""));

        var observation = BybitRealtimeObservationMapper.MapWallet(update, AccountId);

        observation.Kind.Should().Be(PrivateWalletObservationKind.PartialUpdate);
        var balance = observation.Balances.Should().ContainSingle().Subject;
        balance.TotalWalletBalance.Should().Be(12_000m);
        balance.TotalEquity.Should().BeNull();
        balance.TotalAvailableBalance.Should().BeNull();
        balance.TotalPerpUnrealizedPnl.Should().BeNull();
        var coin = balance.Coins.Should().ContainSingle().Subject;
        coin.Equity.Should().Be(0.5m);
        coin.WalletBalance.Should().BeNull();
        coin.UsdValue.Should().BeNull();
        coin.Locked.Should().BeNull();
        coin.UnrealizedPnl.Should().BeNull();
    }

    [Fact]
    public async Task MapWallet_Without_CreationTime_Is_Not_Latency_Measurable()
    {
        var update = await CaptureWalletAsync(PrivateMessage("wallet", "bad", """{"accountType":"UNIFIED","totalEquity":"1"}"""));

        var observation = BybitRealtimeObservationMapper.MapWallet(update, AccountId);

        observation.MessageCreatedAt.Should().BeNull();
        observation.IsLatencyMeasurable.Should().BeFalse();
        observation.Balances.Should().ContainSingle();
    }

    [Fact]
    public async Task MapMarkPrice_Maps_Snapshot_And_Delta_Without_Mark_Price()
    {
        var updates = await CaptureTickersAsync(
            """{"topic":"tickers.BTCUSDT","type":"snapshot","data":{"symbol":"BTCUSDT","markPrice":"17217.33"},"cs":1,"ts":1673272861686}""",
            """{"topic":"tickers.BTCUSDT","type":"delta","data":{"symbol":"BTCUSDT","lastPrice":"1"},"cs":2,"ts":1673272861786}""");

        var observations = updates.Select(BybitRealtimeObservationMapper.MapMarkPrice).ToArray();

        observations.Should().HaveCount(2);
        var snapshot = observations[0]!;
        snapshot.Exchange.Should().Be(ExchangeId.Bybit);
        snapshot.Category.Should().Be(MarketCategory.Linear);
        snapshot.Symbol.Should().Be("BTCUSDT");
        snapshot.UpdateKind.Should().Be(MarketPriceUpdateKind.Snapshot);
        snapshot.MarkPrice.Should().Be(17217.33m);
        snapshot.ObservedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1673272861686));
        var delta = observations[1]!;
        delta.UpdateKind.Should().Be(MarketPriceUpdateKind.Delta);
        delta.MarkPrice.Should().BeNull();
        delta.ObservedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1673272861786));
    }

    [Fact]
    public void MapMarkPrice_Returns_Null_For_Invalid_Symbol()
    {
        var update = new DataEvent<BybitLinearTickerUpdate>(
            "Bybit",
            new BybitLinearTickerUpdate { Symbol = "btc usdt", MarkPrice = 1m },
            DateTime.UtcNow,
            null);

        BybitRealtimeObservationMapper.MapMarkPrice(update).Should().BeNull();
    }

    private static PrivatePositionObservation MapSingle(
        DataEvent<BybitPositionUpdate[]> update,
        Func<string, SettlementAsset?>? resolveSettlement = null) =>
        Map(update, resolveSettlement).Should().ContainSingle().Subject;

    private static IReadOnlyList<PrivatePositionObservation> Map(
        DataEvent<BybitPositionUpdate[]> update,
        Func<string, SettlementAsset?>? resolveSettlement = null) =>
        BybitRealtimeObservationMapper.MapPositions(update, AccountId, resolveSettlement ?? ResolveKnown);

    private static SettlementAsset? ResolveKnown(string symbol) =>
        symbol switch
        {
            "BTCUSDT" => Usdt,
            "BTCPERP" => Usdc,
            _ => null,
        };

    private static void AssertRejected(
        PrivatePositionObservation observation,
        PrivatePositionObservationKind kind,
        PrivatePositionObservationIssue issue)
    {
        observation.Kind.Should().Be(kind);
        observation.Issue.Should().Be(issue);
        observation.Position.Should().BeNull();
    }

    private static DataEvent<BybitPositionUpdate[]> PositionEvent(string? originalData, params BybitPositionUpdate[] positions) =>
        new("Bybit", positions, DateTime.UtcNow, originalData);

    private static BybitPositionUpdate ValidPosition() =>
        new()
        {
            Symbol = "BTCUSDT",
            Category = Category.Linear,
            PositionIdx = PositionIdx.OneWayMode,
            Side = BybitPositionSide.Buy,
            Quantity = 1m,
            Sequence = 1,
        };
}
