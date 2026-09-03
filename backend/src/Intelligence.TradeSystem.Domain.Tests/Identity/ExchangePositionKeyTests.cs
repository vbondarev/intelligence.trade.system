namespace Intelligence.TradeSystem.Domain.Tests.Identity;

public sealed class ExchangePositionKeyTests
{
    private static ExchangePositionKey CreateKey(
        ExchangeAccountId? exchangeAccountId = null,
        string instrument = "BTCUSDT",
        PositionSide side = PositionSide.Long,
        int positionIdx = 1) =>
        ExchangePositionKey.Create(
            exchangeAccountId ?? ExchangeAccountIdFixture,
            InstrumentId.From(instrument),
            side,
            positionIdx);

    private static readonly ExchangeAccountId ExchangeAccountIdFixture = ExchangeAccountId.New();

    [Fact]
    public void Keys_With_Same_Components_Are_Equal()
    {
        var first = CreateKey();
        var second = CreateKey();

        first.Should().Be(second);
    }

    [Fact]
    public void Keys_With_Different_ExchangeAccountId_Are_Not_Equal()
    {
        var first = CreateKey(exchangeAccountId: ExchangeAccountId.New());
        var second = CreateKey(exchangeAccountId: ExchangeAccountId.New());

        first.Should().NotBe(second);
    }

    [Fact]
    public void Keys_With_Different_InstrumentId_Are_Not_Equal()
    {
        var first = CreateKey(instrument: "BTCUSDT");
        var second = CreateKey(instrument: "ETHUSDT");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Keys_With_Different_PositionSide_Are_Not_Equal()
    {
        var first = CreateKey(side: PositionSide.Long);
        var second = CreateKey(side: PositionSide.Short);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Keys_With_Different_PositionIdx_Are_Not_Equal()
    {
        var first = CreateKey(positionIdx: 1);
        var second = CreateKey(positionIdx: 2);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Create_Rejects_Negative_PositionIdx()
    {
        var act = () => CreateKey(positionIdx: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Create_Accepts_Non_Negative_PositionIdx(int positionIdx)
    {
        var act = () => CreateKey(positionIdx: positionIdx);

        act.Should().NotThrow();
    }
}
