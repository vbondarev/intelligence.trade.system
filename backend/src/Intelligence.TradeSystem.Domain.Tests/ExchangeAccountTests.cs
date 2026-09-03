namespace Intelligence.TradeSystem.Domain.Tests;

public sealed class ExchangeAccountTests
{
    [Fact]
    public void Create_Saves_Account_Data()
    {
        var id = ExchangeAccountId.New();
        var userId = UserId.New();

        var account = ExchangeAccount.Create(
            id,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

        account.Id.Should().Be(id);
        account.UserId.Should().Be(userId);
        account.ExchangeId.Should().Be(ExchangeId.Bybit);
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        account.Capabilities.Should().HaveFlag(ExchangeAccountCapabilities.ReadBalance);
        account.Capabilities.Should().HaveFlag(ExchangeAccountCapabilities.ReadPositions);
        account.LastSyncedAt.Should().BeNull();
        account.LastError.Should().BeNull();
    }

    [Fact]
    public void Create_Rejects_Default_Identity()
    {
        var actUser = () => ExchangeAccount.Create(ExchangeAccountId.New(), default, ExchangeId.Bybit);
        var actAccount = () => ExchangeAccount.Create(default, UserId.New(), ExchangeId.Bybit);

        actUser.Should().Throw<ArgumentException>();
        actAccount.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Rejects_Unknown_Exchange()
    {
        var act = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), (ExchangeId)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Unknown_Capabilities()
    {
        var act = () => ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            capabilities: (ExchangeAccountCapabilities)4);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Undefined_ConnectionStatus()
    {
        var act = () => ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            connectionStatus: (ExchangeAccountConnectionStatus)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Identity_Properties_Cannot_Be_Publicly_Assigned()
    {
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.Id))!.SetMethod.Should().BeNull();
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.UserId))!.SetMethod.Should().BeNull();
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.ExchangeId))!.SetMethod.Should().BeNull();
    }
}
