using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain.Tests.Identity;

public sealed class SettlementAssetTests
{
    [Theory]
    [InlineData("USDT")]
    [InlineData("USDC")]
    [InlineData("BTC")]
    public void From_Creates_Asset_Without_Restricting_To_Known_Values(string value)
    {
        SettlementAsset.From(value).Value.Should().Be(value);
    }

    [Fact]
    public void From_Trims_Surrounding_Whitespace()
    {
        SettlementAsset.From("  USDC  ").Value.Should().Be("USDC");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void From_Rejects_Missing_Value(string? value)
    {
        var act = () => SettlementAsset.From(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Instances_Are_Compared_By_Value()
    {
        SettlementAsset.From("USDT").Should().Be(SettlementAsset.From("USDT"));
        SettlementAsset.From("USDT").Should().NotBe(SettlementAsset.From("USDC"));
    }

    [Fact]
    public void ToString_Returns_Value()
    {
        SettlementAsset.From("USDC").ToString().Should().Be("USDC");
    }
}
