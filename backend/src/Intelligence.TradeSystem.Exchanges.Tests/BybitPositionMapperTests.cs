using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using FluentAssertions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class BybitPositionMapperTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MapOpenPosition_Preserves_PositionIdx(int positionIdx)
    {
        var bybitPosition = new BybitPosition
        {
            Symbol = "BTCUSDT",
            PositionIdx = (PositionIdx)positionIdx,
        };

        var mapped = bybitPosition.MapOpenPosition(MarketCategory.Linear);

        mapped.PositionIdx.Should().Be(positionIdx);
    }
}
