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
            ProviderIdentity,
            "Основной",
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
        account.ProviderIdentity.Should().Be(ProviderIdentity);
        account.DisplayName.Should().Be("Основной");
    }

    [Fact]
    public void Create_Trims_DisplayName_And_Preserves_Its_Case()
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), ExchangeId.Bybit, ProviderIdentity, "  GinArea Main  ");

        account.DisplayName.Should().Be("GinArea Main");
    }

    [Fact]
    public void Create_Accepts_DisplayName_At_The_Length_Limit_After_Trimming()
    {
        var displayName = new string('a', ExchangeAccount.DisplayNameMaxLength);

        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), ExchangeId.Bybit, ProviderIdentity, $"  {displayName}  ");

        account.DisplayName.Should().Be(displayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_Rejects_Blank_DisplayName(string displayName)
    {
        var act = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), ExchangeId.Bybit, ProviderIdentity, displayName);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("displayName");
    }

    [Fact]
    public void Create_Rejects_Null_And_Too_Long_DisplayName()
    {
        var actNull = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), ExchangeId.Bybit, ProviderIdentity, null!);
        var actTooLong = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), ExchangeId.Bybit, ProviderIdentity,
            new string('a', ExchangeAccount.DisplayNameMaxLength + 1));

        actNull.Should().Throw<ArgumentNullException>();
        actTooLong.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("displayName");
    }

    [Fact]
    public void Rename_Changes_Only_The_Normalized_DisplayName()
    {
        var id = ExchangeAccountId.New();
        var userId = UserId.New();
        var syncedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var balanceObservationAt = syncedAt.AddMinutes(1);
        var positionsObservationAt = syncedAt.AddMinutes(2);
        var account = ExchangeAccount.Create(
            id,
            userId,
            ExchangeId.Bybit,
            ProviderIdentity,
            "Основной",
            ExchangeAccountConnectionStatus.Unavailable,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            syncedAt,
            "positions_failed",
            balanceObservationAt,
            positionsObservationAt);

        var changed = account.Rename("  GinArea  ");

        changed.Should().BeTrue();
        account.DisplayName.Should().Be("GinArea");
        account.Id.Should().Be(id);
        account.UserId.Should().Be(userId);
        account.ExchangeId.Should().Be(ExchangeId.Bybit);
        account.ProviderIdentity.Should().Be(ProviderIdentity);
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        account.Capabilities.Should().Be(
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        account.LastSyncedAt.Should().Be(syncedAt);
        account.LastError.Should().Be("positions_failed");
        account.LastAppliedBalanceObservationAt.Should().Be(balanceObservationAt);
        account.LastAppliedPositionsObservationAt.Should().Be(positionsObservationAt);
    }

    [Fact]
    public void Rename_Reports_No_Change_When_The_Normalized_Value_Is_The_Same()
    {
        var account = CreateAccount();

        account.Rename(" Основной ").Should().BeFalse();
        account.DisplayName.Should().Be("Основной");
    }

    [Fact]
    public void Rename_Is_Case_Sensitive()
    {
        var account = CreateAccount();

        account.Rename("основной").Should().BeTrue();
        account.DisplayName.Should().Be("основной");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Rejects_Invalid_DisplayName_Without_Changing_It(string displayName)
    {
        var account = CreateAccount();

        var actBlank = () => account.Rename(displayName);
        var actTooLong = () => account.Rename(new string('a', ExchangeAccount.DisplayNameMaxLength + 1));
        var actNull = () => account.Rename(null!);

        actBlank.Should().Throw<ArgumentException>();
        actTooLong.Should().Throw<ArgumentException>();
        actNull.Should().Throw<ArgumentNullException>();
        account.DisplayName.Should().Be("Основной");
    }

    [Fact]
    public void Rename_Is_Allowed_For_Disabled_Account_And_Keeps_It_Disabled()
    {
        var account = CreateAccount(ExchangeAccountConnectionStatus.Connected);
        account.Disable();

        account.Rename("Архив").Should().BeTrue();

        account.DisplayName.Should().Be("Архив");
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
    }

    [Fact]
    public void Disable_And_Reconnect_Preserve_DisplayName()
    {
        var account = CreateAccount(ExchangeAccountConnectionStatus.Connected);
        account.Rename("GinArea");

        account.Disable();
        account.DisplayName.Should().Be("GinArea");

        account.Reconnect();
        account.DisplayName.Should().Be("GinArea");
    }

    [Fact]
    public void DisplayName_Cannot_Be_Publicly_Assigned() =>
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.DisplayName))!.SetMethod!.IsPublic.Should().BeFalse();

    [Fact]
    public void Create_Rejects_Default_Identity()
    {
        var actUser = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), default, ExchangeId.Bybit, ProviderIdentity, "Основной");
        var actAccount = () => ExchangeAccount.Create(
            default, UserId.New(), ExchangeId.Bybit, ProviderIdentity, "Основной");
        var actProvider = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), ExchangeId.Bybit, default, "Основной");

        actUser.Should().Throw<ArgumentException>();
        actAccount.Should().Throw<ArgumentException>();
        actProvider.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Rejects_Unknown_Exchange()
    {
        var act = () => ExchangeAccount.Create(
            ExchangeAccountId.New(), UserId.New(), (ExchangeId)999, ProviderIdentity, "Основной");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Rejects_Unknown_Capabilities()
    {
        var act = () => ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ProviderIdentity,
            "Основной",
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
            ProviderIdentity,
            "Основной",
            connectionStatus: (ExchangeAccountConnectionStatus)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Identity_Properties_Cannot_Be_Publicly_Assigned()
    {
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.Id))!.SetMethod.Should().BeNull();
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.UserId))!.SetMethod.Should().BeNull();
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.ExchangeId))!.SetMethod.Should().BeNull();
        typeof(ExchangeAccount).GetProperty(nameof(ExchangeAccount.ProviderIdentity))!.SetMethod.Should().BeNull();
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
    public void Reconnect_restores_disabled_account()
    {
        var account = CreateAccount(ExchangeAccountConnectionStatus.Connected);
        account.Disable();

        account.Reconnect();

        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        account.LastError.Should().BeNull();
    }

    [Fact]
    public void Reconnect_preserves_identity_capabilities_and_history()
    {
        var id = ExchangeAccountId.New();
        var userId = UserId.New();
        var syncedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var balanceObservationAt = syncedAt.AddMinutes(1);
        var positionsObservationAt = syncedAt.AddMinutes(2);
        var account = ExchangeAccount.Create(
            id,
            userId,
            ExchangeId.Bybit,
            ProviderIdentity,
            "Основной",
            ExchangeAccountConnectionStatus.Unavailable,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            syncedAt,
            "positions_failed",
            balanceObservationAt,
            positionsObservationAt);
        account.Disable();

        account.Reconnect();

        account.Id.Should().Be(id);
        account.UserId.Should().Be(userId);
        account.ExchangeId.Should().Be(ExchangeId.Bybit);
        account.ProviderIdentity.Should().Be(ProviderIdentity);
        account.Capabilities.Should().Be(
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        account.LastSyncedAt.Should().Be(syncedAt);
        account.LastAppliedBalanceObservationAt.Should().Be(balanceObservationAt);
        account.LastAppliedPositionsObservationAt.Should().Be(positionsObservationAt);
        account.LastError.Should().BeNull();
    }

    [Theory]
    [InlineData(ExchangeAccountConnectionStatus.Unknown)]
    [InlineData(ExchangeAccountConnectionStatus.Connected)]
    [InlineData(ExchangeAccountConnectionStatus.Unavailable)]
    public void Reconnect_rejects_non_disabled_account(ExchangeAccountConnectionStatus status)
    {
        var account = CreateAccount(status);

        var act = () => account.Reconnect();

        act.Should().Throw<InvalidOperationException>();
        account.ConnectionStatus.Should().Be(status);
    }

    [Fact]
    public void MarkConnected_still_cannot_reactivate_disabled_account()
    {
        var account = CreateAccount(ExchangeAccountConnectionStatus.Disabled);

        var act = () => account.MarkConnected();

        act.Should().Throw<InvalidOperationException>();
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
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
            ProviderIdentity,
            "Основной",
            status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static readonly ExchangeAccountProviderIdentity ProviderIdentity =
        ExchangeAccountProviderIdentity.From("provider-account");
}
