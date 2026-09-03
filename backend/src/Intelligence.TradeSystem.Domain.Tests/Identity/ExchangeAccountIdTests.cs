namespace Intelligence.TradeSystem.Domain.Tests.Identity;

public sealed class ExchangeAccountIdTests
{
    [Fact]
    public void Two_Instances_With_Same_Guid_Are_Equal()
    {
        var guid = Guid.NewGuid();

        var first = ExchangeAccountId.FromGuid(guid);
        var second = ExchangeAccountId.FromGuid(guid);

        first.Should().Be(second);
    }

    [Fact]
    public void Instances_With_Different_Guid_Are_Not_Equal()
    {
        var first = ExchangeAccountId.FromGuid(Guid.NewGuid());
        var second = ExchangeAccountId.FromGuid(Guid.NewGuid());

        first.Should().NotBe(second);
    }

    [Fact]
    public void New_Creates_Non_Empty_Value()
    {
        var id = ExchangeAccountId.New();

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void FromGuid_Rejects_Empty_Guid()
    {
        var act = () => ExchangeAccountId.FromGuid(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_Returns_Underlying_Guid_Representation()
    {
        var guid = Guid.NewGuid();
        var id = ExchangeAccountId.FromGuid(guid);

        id.ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void Two_Accounts_On_Same_Exchange_Have_Different_Ids()
    {
        var accountA = ExchangeAccountId.New();
        var accountB = ExchangeAccountId.New();

        accountA.Should().NotBe(accountB);
    }
}
