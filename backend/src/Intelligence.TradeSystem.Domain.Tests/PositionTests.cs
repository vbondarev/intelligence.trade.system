using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain.Tests;

public sealed class PositionTests
{
    private static readonly ExchangePositionKey Key = ExchangePositionKey.Create(
        ExchangeAccountId.New(), InstrumentId.From("BTCUSDT"), PositionSide.Long, 1);
    private static readonly DateTimeOffset FirstDetectedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Saves_Position_State_And_Generates_New_Id()
    {
        var position = Position.Create(
            Key,
            MarketCategory.Linear,
            SettlementAsset.From("USDT"),
            2m,
            FirstDetectedAt,
            FirstDetectedAt.AddMinutes(1),
            averageEntryPrice: 100m,
            positionValue: 200m,
            leverage: 2m,
            markPrice: 101m,
            unrealizedPnl: -3m);

        position.Id.Should().NotBe(default);
        position.ExchangePositionKey.Should().Be(Key);
        position.MarketCategory.Should().Be(MarketCategory.Linear);
        position.Size.Should().Be(2m);
        position.AverageEntryPrice.Should().Be(100m);
        position.PositionValue.Should().Be(200m);
        position.Leverage.Should().Be(2m);
        position.MarkPrice.Should().Be(101m);
        position.UnrealizedPnl.Should().Be(-3m);
    }

    [Fact]
    public void Create_Accepts_Inverse_MarketCategory()
    {
        var position = Position.Create(Key, MarketCategory.Inverse, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        position.MarketCategory.Should().Be(MarketCategory.Inverse);
    }

    [Fact]
    public void Create_Rejects_Spot_MarketCategory()
    {
        var act = () => Position.Create(Key, MarketCategory.Spot, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Undefined_MarketCategory()
    {
        var act = () => Position.Create(Key, (MarketCategory)999, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Reopened_Position_With_Same_Exchange_Key_Gets_New_Id()
    {
        var first = Position.Create(Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);
        var second = Position.Create(Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        second.Id.Should().NotBe(first.Id);
        second.ExchangePositionKey.Should().Be(first.ExchangePositionKey);
    }

    [Fact]
    public void Create_Preserves_Unknown_Values_As_Null()
    {
        var position = Position.Create(Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        position.AverageEntryPrice.Should().BeNull();
        position.MarkPrice.Should().BeNull();
        position.LiquidationPrice.Should().BeNull();
        position.UnrealizedPnl.Should().BeNull();
        position.TakeProfit.Should().BeNull();
        position.StopLoss.Should().BeNull();
        position.Leverage.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Rejects_Non_Positive_Size(decimal size) =>
        FluentActions.Invoking(() => Position.Create(Key, MarketCategory.Linear, SettlementAsset.From("USDT"), size, FirstDetectedAt, FirstDetectedAt))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Create_Rejects_Default_Key()
    {
        var act = () => Position.Create(default, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    public void Create_Rejects_Negative_Prices(decimal price)
    {
        var act = () => Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt, markPrice: price);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Negative_PositionValue()
    {
        var act = () => Position.Create(
        Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt, positionValue: -100m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Accepts_Null_And_Zero_PositionValue()
    {
        var unknown = Position.Create(Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);
        var zero = Position.Create(
        Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt, positionValue: 0m);

        unknown.PositionValue.Should().BeNull();
        zero.PositionValue.Should().Be(0m);
    }

    [Fact]
    public void Create_Rejects_Non_Positive_Leverage()
    {
        var act = () => Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt, leverage: 0m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Reversed_Observation_Timestamps()
    {
        var act = () => Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt.AddSeconds(-1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Identity_Properties_Cannot_Be_Publicly_Assigned()
    {
        typeof(Position).GetProperty(nameof(Position.Id))!.SetMethod.Should().BeNull();
        typeof(Position).GetProperty(nameof(Position.ExchangePositionKey))!.SetMethod.Should().BeNull();
    }

    [Fact]
    public void Create_Saves_Settlement_Asset()
    {
        var position = Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDC"), 1m, FirstDetectedAt, FirstDetectedAt);

        position.SettlementAsset.Should().Be(SettlementAsset.From("USDC"));
    }

    [Fact]
    public void Create_Rejects_Uninitialized_Settlement_Asset()
    {
        var act = () => Position.Create(Key, MarketCategory.Linear, default, 1m, FirstDetectedAt, FirstDetectedAt);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Restore_Preserves_Settlement_Asset()
    {
        var original = Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDC"), 1m, FirstDetectedAt, FirstDetectedAt);

        var restored = Position.Restore(
            original.Id,
            original.ExchangePositionKey,
            original.MarketCategory,
            original.SettlementAsset,
            original.Size,
            original.FirstDetectedAt,
            original.LastObservedAt,
            original.TrackingState,
            original.ClosedAt,
            original.Changes);

        restored.SettlementAsset.Should().Be(SettlementAsset.From("USDC"));
    }

    [Fact]
    public void Restore_Rejects_Uninitialized_Settlement_Asset()
    {
        var original = Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDT"), 1m, FirstDetectedAt, FirstDetectedAt);

        var act = () => Position.Restore(
            original.Id,
            original.ExchangePositionKey,
            original.MarketCategory,
            default,
            original.Size,
            original.FirstDetectedAt,
            original.LastObservedAt,
            original.TrackingState,
            original.ClosedAt,
            original.Changes);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplyObservation_Does_Not_Change_Settlement_Asset()
    {
        var position = Position.Create(
            Key, MarketCategory.Linear, SettlementAsset.From("USDC"), 1m, FirstDetectedAt, FirstDetectedAt);

        position.ApplyObservation(2m, FirstDetectedAt.AddMinutes(1), positionValue: 200m);

        position.SettlementAsset.Should().Be(SettlementAsset.From("USDC"));
        typeof(Position).GetProperty(nameof(Position.SettlementAsset))!.SetMethod.Should().BeNull();
    }
}
