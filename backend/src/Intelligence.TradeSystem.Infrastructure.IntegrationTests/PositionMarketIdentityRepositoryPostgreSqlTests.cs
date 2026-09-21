using System.Data.Common;
using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class PositionMarketIdentityRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Identity_is_user_scoped_and_supports_active_and_closed_positions()
    {
        var owner = CreateAccount(UserId.New());
        var foreign = CreateAccount(UserId.New());
        var active = CreatePosition(owner.Id, "BTCUSDT", MarketCategory.Linear);
        var closed = CreatePosition(owner.Id, "ETHUSDT", MarketCategory.Inverse);
        closed.Close(T0.AddMinutes(1));
        var foreignPosition = CreatePosition(foreign.Id, "FOREIGN", MarketCategory.Linear);

        await Persist(owner, [active, closed]);
        await Persist(foreign, [foreignPosition]);

        await using var context = await CreateMigratedContext();
        var repository = new PositionMarketIdentityRepository(context);

        var activeIdentity = await repository.GetAsync(owner.UserId, active.Id);
        var closedIdentity = await repository.GetAsync(owner.UserId, closed.Id);
        var foreignIdentity = await repository.GetAsync(owner.UserId, foreignPosition.Id);
        var missingIdentity = await repository.GetAsync(owner.UserId, PositionId.New());

        Assert.Equal(
            new PositionMarketIdentity(
                active.Id,
                owner.Id,
                ExchangeId.Bybit,
                "BTCUSDT",
                MarketCategory.Linear),
            activeIdentity);
        Assert.Equal(
            new PositionMarketIdentity(
                closed.Id,
                owner.Id,
                ExchangeId.Bybit,
                "ETHUSDT",
                MarketCategory.Inverse),
            closedIdentity);
        Assert.Null(foreignIdentity);
        Assert.Null(missingIdentity);
    }

    [Fact]
    public async Task Identity_query_is_projection_only_and_does_not_read_private_or_history_tables()
    {
        var account = CreateAccount(UserId.New());
        var position = CreatePosition(account.Id, "BTCUSDT", MarketCategory.Linear);
        await Persist(account, [position]);

        var capture = new CommandCaptureInterceptor();
        await using var context = new TradeSystemDbContext(
            new DbContextOptionsBuilder<TradeSystemDbContext>()
                .UseNpgsql(
                    fixture.ConnectionString,
                    npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                        typeof(TradeSystemDbContext).Assembly.GetName().Name))
                .AddInterceptors(capture)
                .Options);
        await context.Database.MigrateAsync();
        capture.Commands.Clear();

        var repository = new PositionMarketIdentityRepository(context);
        var result = await repository.GetAsync(account.UserId, position.Id);

        Assert.NotNull(result);
        Assert.Contains(capture.Commands, command =>
            command.Contains("positions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, command =>
            command.Contains("exchange_accounts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("position_changes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("exchange_account_credentials", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("portfolio_states", StringComparison.OrdinalIgnoreCase));
    }

    private async Task Persist(
        ExchangeAccount account,
        IReadOnlyCollection<Position> positions)
    {
        await using var context = await CreateMigratedContext();
        await new ExchangeAccountRepository(context)
            .SaveAsync(account.UserId, account, expectedVersion: null);
        foreach (var position in positions)
        {
            await new PositionRepository(context)
                .SaveAsync(account.UserId, position, expectedVersion: null);
        }
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static ExchangeAccount CreateAccount(UserId userId) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            T0);

    private static Position CreatePosition(
        ExchangeAccountId accountId,
        string symbol,
        MarketCategory category) =>
        Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From(symbol),
                PositionSide.Long,
                0),
            category,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
