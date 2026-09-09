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
        account.LastAppliedBalanceObservationAt.Should().BeNull();
        account.LastAppliedPositionsObservationAt.Should().BeNull();
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

    [Fact]
    public void MarkConnected_Clears_Previous_Error_And_Changes_Status()
    {
        var account = CreateAccount(ExchangeAccountConnectionStatus.Unavailable);
        account.MarkConnected();

        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        account.LastError.Should().BeNull();
    }

    [Fact]
    public void Sync_State_Transitions_Update_Timestamp_And_Error()
    {
        var account = CreateAccount();
        var syncedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        account.RecordSuccessfulSync(syncedAt);
        account.RecordSyncFailure("temporary exchange failure");

        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        account.LastSyncedAt.Should().Be(syncedAt);
        account.LastError.Should().Be("temporary exchange failure");
    }

    [Fact]
    public void RecordSyncFailure_Preserves_Last_Successful_Sync_Time()
    {
        var account = CreateAccount();
        var syncedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        account.RecordSuccessfulSync(syncedAt);
        account.RecordSyncFailure("positions_failed");

        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        account.LastSyncedAt.Should().Be(syncedAt);
        account.LastError.Should().Be("positions_failed");
    }

    [Fact]
    public void RecordSuccessfulSync_Recovers_From_Unavailable_And_Clears_Error()
    {
        var account = CreateAccount();
        account.RecordSuccessfulSync(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        account.RecordSyncFailure("balance_failed");
        var recoveredAt = new DateTimeOffset(2026, 9, 8, 12, 5, 0, TimeSpan.Zero);

        account.RecordSuccessfulSync(recoveredAt);

        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        account.LastSyncedAt.Should().Be(recoveredAt);
        account.LastError.Should().BeNull();
    }

    [Fact]
    public void Disable_Is_Idempotent_And_Prevents_Reactivation()
    {
        var account = CreateAccount(ExchangeAccountConnectionStatus.Connected);

        account.Disable();
        account.Disable();

        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
        var act = () => account.MarkConnected();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkUnavailable_Rejects_Empty_Error()
    {
        var account = CreateAccount();

        var act = () => account.MarkUnavailable(" ");

        act.Should().Throw<ArgumentException>();
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unknown);
    }

    [Fact]
    public void Observation_Watermark_Is_Monotonic_And_Classifies_Replays()
    {
        var account = CreateAccount();
        var t1 = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(1);

        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Balance, t1)
            .Should().Be(ExchangeAccountObservationDisposition.Applied);
        account.LastAppliedBalanceObservationAt.Should().Be(t1);

        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Balance, t2)
            .Should().Be(ExchangeAccountObservationDisposition.Applied);
        account.LastAppliedBalanceObservationAt.Should().Be(t2);

        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Balance, t2)
            .Should().Be(ExchangeAccountObservationDisposition.AlreadyApplied);
        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Balance, t1)
            .Should().Be(ExchangeAccountObservationDisposition.Superseded);
        account.LastAppliedBalanceObservationAt.Should().Be(t2);

        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Positions, t1)
            .Should().Be(ExchangeAccountObservationDisposition.Applied);
        account.LastAppliedPositionsObservationAt.Should().Be(t1);
        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Positions, t1)
            .Should().Be(ExchangeAccountObservationDisposition.AlreadyApplied);
        account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Positions, t1.AddMinutes(-1))
            .Should().Be(ExchangeAccountObservationDisposition.Superseded);
    }

    [Fact]
    public void Newer_Failure_Advances_Watermark_And_Disabled_Account_Cannot_Recover()
    {
        var account = CreateAccount();
        var observationAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        account.AdvanceObservationWatermark(
                ExchangeAccountObservationResource.Positions,
                observationAt)
            .Should().Be(ExchangeAccountObservationDisposition.Applied);
        account.RecordSyncFailure("positions_failed");
        account.LastAppliedPositionsObservationAt.Should().Be(observationAt);
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);

        account.Disable();
        var act = () => account.AdvanceObservationWatermark(
            ExchangeAccountObservationResource.Positions,
            observationAt.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
        account.LastAppliedPositionsObservationAt.Should().Be(observationAt);
    }

    private static ExchangeAccount CreateAccount(
        ExchangeAccountConnectionStatus status = ExchangeAccountConnectionStatus.Unknown) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
}
