using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class PositionReadRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_is_user_scoped_and_applies_database_side_filters_and_cursor_order()
    {
        var owner = CreateAccount(UserId.New());
        var foreign = CreateAccount(UserId.New());
        var active = CreatePosition(owner.Id, "BTCUSDT", T0);
        var closed = CreatePosition(owner.Id, "CLOSEDUSDT", T0.AddMinutes(-1));
        closed.Close(T0.AddMinutes(1));
        var percentSymbol = CreatePosition(owner.Id, "A%B", T0.AddMinutes(-2));
        var percentWildcardCandidate = CreatePosition(owner.Id, "AXB", T0.AddMinutes(-3));
        var underscoreSymbol = CreatePosition(owner.Id, "A_B", T0.AddMinutes(-4));
        var underscoreWildcardCandidate = CreatePosition(owner.Id, "ACB", T0.AddMinutes(-5));
        var foreignPosition = CreatePosition(foreign.Id, "FOREIGN", T0.AddMinutes(1));

        await Persist(owner, [active, closed, percentSymbol, percentWildcardCandidate,
            underscoreSymbol, underscoreWildcardCandidate]);
        await Persist(foreign, [foreignPosition]);

        await using var context = await CreateMigratedContext();
        var repository = new PositionReadRepository(context);

        var defaultPage = await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, null, null, null, 50, null));
        var defaultIds = defaultPage.Items.Select(item => item.Id).ToArray();

        Assert.Contains(active.Id, defaultIds);
        Assert.DoesNotContain(closed.Id, defaultIds);
        Assert.DoesNotContain(foreignPosition.Id, defaultIds);
        Assert.Empty((await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(foreign.Id, null, null, null, 50, null))).Items);
        Assert.Empty((await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(ExchangeAccountId.New(), null, null, null, 50, null))).Items);

        var closedPage = await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, PositionTrackingState.Closed, null, null, 50, null));
        Assert.Equal([closed.Id], closedPage.Items.Select(item => item.Id));

        var percentPage = await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, null, "a%b", null, 50, null));
        Assert.Equal([percentSymbol.Id], percentPage.Items.Select(item => item.Id));

        var underscorePage = await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, null, "a_b", null, 50, null));
        Assert.Equal([underscoreSymbol.Id], underscorePage.Items.Select(item => item.Id));

        var firstPage = await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, null, null, null, 2, null));
        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await repository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, null, null, null, 2, firstPage.NextCursor));
        Assert.Equal(
            firstPage.Items.Select(item => item.Id).Concat(secondPage.Items.Select(item => item.Id)).Distinct().ToArray(),
            firstPage.Items.Select(item => item.Id).Concat(secondPage.Items.Select(item => item.Id)).ToArray());
    }

    [Fact]
    public async Task List_composes_owned_account_symbol_side_and_tracking_state_filters()
    {
        var userId = UserId.New();
        var accountA = CreateAccount(userId);
        var accountB = CreateAccount(userId);
        var foreign = CreateAccount(UserId.New());
        var accountALong = CreatePosition(accountA.Id, "BTCUSDT", T0, PositionSide.Long);
        var accountAShort = CreatePosition(accountA.Id, "BTCUSDT", T0.AddMinutes(-1), PositionSide.Short);
        var accountBPosition = CreatePosition(accountB.Id, "BTCUSDT", T0, PositionSide.Long);
        var foreignPosition = CreatePosition(foreign.Id, "BTCUSDT", T0, PositionSide.Long);

        await Persist(accountA, [accountALong, accountAShort]);
        await Persist(accountB, [accountBPosition]);
        await Persist(foreign, [foreignPosition]);

        await using var context = await CreateMigratedContext();
        var repository = new PositionReadRepository(context);

        var accountALongPage = await repository.ListAsync(
            userId,
            PositionReadQuery.Create(
                accountA.Id,
                PositionTrackingState.Active,
                "btcusdt",
                PositionSide.Long,
                50,
                null));
        var accountAShortPage = await repository.ListAsync(
            userId,
            PositionReadQuery.Create(
                accountA.Id,
                PositionTrackingState.Active,
                "BTCUSDT",
                PositionSide.Short,
                50,
                null));
        var accountBPage = await repository.ListAsync(
            userId,
            PositionReadQuery.Create(
                accountB.Id,
                null,
                null,
                null,
                50,
                null));

        Assert.Equal([accountALong.Id], accountALongPage.Items.Select(item => item.Id));
        Assert.Equal([accountAShort.Id], accountAShortPage.Items.Select(item => item.Id));
        Assert.Equal([accountBPosition.Id], accountBPage.Items.Select(item => item.Id));
        Assert.DoesNotContain(foreignPosition.Id, accountALongPage.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Cursor_traversal_returns_every_position_once_and_uses_id_desc_tie_breaker()
    {
        var owner = CreateAccount(UserId.New());
        var tiedPositions = Enumerable.Range(0, 4)
            .Select(index => CreatePosition(owner.Id, $"TIED{index}", T0))
            .ToArray();
        var positions = tiedPositions
            .Concat(
            [
                CreatePosition(owner.Id, "OLDER1", T0.AddMinutes(-1)),
                CreatePosition(owner.Id, "OLDER2", T0.AddMinutes(-2)),
            ])
            .ToArray();
        await Persist(owner, positions);

        var expected = positions
            .OrderByDescending(position => position.FirstDetectedAt)
            .ThenByDescending(position => position.Id.Value)
            .Select(position => position.Id)
            .ToArray();
        var actual = new List<PositionId>();
        PositionReadCursor? cursor = null;
        var hasMore = true;

        await using var context = await CreateMigratedContext();
        var repository = new PositionReadRepository(context);
        while (hasMore)
        {
            var page = await repository.ListAsync(
                owner.UserId,
                PositionReadQuery.Create(null, null, null, null, 2, cursor));
            actual.AddRange(page.Items.Select(item => item.Id));
            hasMore = page.HasMore;
            cursor = page.NextCursor;
        }

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, actual.Distinct().Count());
        Assert.Equal(
            expected[..tiedPositions.Length],
            actual.Take(tiedPositions.Length).ToArray());
    }

    [Fact]
    public async Task Detail_and_portfolio_reads_hide_foreign_resources_and_do_not_load_history_tables()
    {
        var owner = CreateAccount(UserId.New());
        var foreign = CreateAccount(UserId.New());
        var ownerPosition = CreatePosition(owner.Id, "BTCUSDT", T0);
        var foreignPosition = CreatePosition(foreign.Id, "ETHUSDT", T0);
        var portfolio = PortfolioState.Create(
            owner.Id,
            [ownerPosition],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        await Persist(owner, [ownerPosition], portfolio);
        await Persist(foreign, [foreignPosition]);

        await using var detailContext = await CreateMigratedContext();
        var detailRepository = new PositionReadRepository(detailContext);
        Assert.NotNull(await detailRepository.GetByIdAsync(owner.UserId, ownerPosition.Id));
        Assert.Null(await detailRepository.GetByIdAsync(foreign.UserId, ownerPosition.Id));

        await using var portfolioContext = await CreateMigratedContext();
        var portfolioRepository = new PortfolioReadRepository(portfolioContext);
        var summary = await portfolioRepository.GetLatestAsync(owner.UserId, owner.Id);
        var foreignSummary = await portfolioRepository.GetLatestAsync(foreign.UserId, owner.Id);
        var missingSummary = await portfolioRepository.GetLatestAsync(owner.UserId, ExchangeAccountId.New());

        Assert.True(summary.AccountExists);
        Assert.NotNull(summary.Summary);
        Assert.Equal(1000m, summary.Summary!.TotalEquity);
        Assert.False(foreignSummary.AccountExists);
        Assert.Null(foreignSummary.Summary);
        Assert.False(missingSummary.AccountExists);
    }

    [Fact]
    public async Task Portfolio_read_distinguishes_owned_without_snapshot_and_returns_latest_snapshot()
    {
        var withoutSnapshot = CreateAccount(UserId.New());
        await Persist(withoutSnapshot, []);

        var owner = CreateAccount(UserId.New());
        var position = CreatePosition(owner.Id, "BTCUSDT", T0);
        var older = PortfolioState.Create(
            owner.Id,
            [position],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0,
            TimeSpan.FromMinutes(5));
        var newer = PortfolioState.Create(
            owner.Id,
            [position],
            new PortfolioCapitalState(2000m, 1500m, T0.AddMinutes(1), 2000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        await Persist(owner, [position], older);

        await using (var context = await CreateMigratedContext())
        {
            await new PortfolioStateRepository(context)
                .SaveAsync(owner.UserId, newer);
        }

        await using var readContext = await CreateMigratedContext();
        var repository = new PortfolioReadRepository(readContext);
        var withoutSnapshotResult = await repository.GetLatestAsync(
            withoutSnapshot.UserId,
            withoutSnapshot.Id);
        var latestResult = await repository.GetLatestAsync(owner.UserId, owner.Id);

        Assert.True(withoutSnapshotResult.AccountExists);
        Assert.Null(withoutSnapshotResult.Summary);
        Assert.True(latestResult.AccountExists);
        Assert.NotNull(latestResult.Summary);
        Assert.Equal(2000m, latestResult.Summary!.TotalEquity);
        Assert.Equal(1500m, latestResult.Summary.AvailableCapital);
        Assert.Equal(T0.AddMinutes(1), latestResult.Summary.CalculatedAt);
    }

    [Fact]
    public async Task Read_queries_are_projection_only_and_have_inspectable_postgresql_plans()
    {
        var owner = CreateAccount(UserId.New());
        var position = CreatePosition(owner.Id, "A%B", T0);
        var portfolio = PortfolioState.Create(
            owner.Id,
            [position],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        await Persist(owner, [position], portfolio);

        var capture = new CommandCaptureInterceptor();
        await using var context = await CreateCapturedContext(capture);
        var positionRepository = new PositionReadRepository(context);
        await positionRepository.ListAsync(
            owner.UserId,
            PositionReadQuery.Create(null, null, "a%b", null, 50, null));
        Assert.Contains(capture.Commands, command =>
            command.Contains("position_id", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("position_changes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Commands, command =>
            command.Contains("lower", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("ILIKE", StringComparison.OrdinalIgnoreCase) ||
            command.Contains(" LIKE ", StringComparison.OrdinalIgnoreCase));

        capture.Commands.Clear();
        var portfolioRepository = new PortfolioReadRepository(context);
        await portfolioRepository.GetLatestAsync(owner.UserId, owner.Id);
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("portfolio_position_states", StringComparison.OrdinalIgnoreCase));

        var plans = await ExplainListQueriesAsync(context, owner.UserId, owner.Id);
        Assert.NotEmpty(plans);
    }

    private async Task Persist(
        ExchangeAccount account,
        IReadOnlyCollection<Position> positions,
        PortfolioState? portfolio = null)
    {
        await using var context = await CreateMigratedContext();
        var accountRepository = new ExchangeAccountRepository(context);
        var positionRepository = new PositionRepository(context);
        await accountRepository.SaveAsync(account.UserId, account, null);
        foreach (var position in positions)
        {
            await positionRepository.SaveAsync(account.UserId, position, null);
        }

        if (portfolio is not null)
        {
            await new PortfolioStateRepository(context)
                .SaveAsync(account.UserId, portfolio);
        }
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private async Task<TradeSystemDbContext> CreateCapturedContext(
        CommandCaptureInterceptor capture)
    {
        var context = new TradeSystemDbContext(
            new DbContextOptionsBuilder<TradeSystemDbContext>()
                .UseNpgsql(
                    fixture.ConnectionString,
                    npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                        typeof(TradeSystemDbContext).Assembly.GetName().Name))
                .AddInterceptors(capture)
                .Options);
        await context.Database.MigrateAsync();
        capture.Commands.Clear();
        return context;
    }

    private static async Task<string[]> ExplainListQueriesAsync(
        TradeSystemDbContext context,
        UserId userId,
        ExchangeAccountId accountId)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN (FORMAT TEXT)
            SELECT p.position_id
            FROM positions AS p
            INNER JOIN exchange_accounts AS a
                ON a.exchange_account_id = p.exchange_account_id
            WHERE a.user_id = @user_id
              AND p.tracking_state IN ('Active', 'Unknown', 'Stale')
            ORDER BY p.first_detected_at DESC, p.position_id DESC
            LIMIT 51;

            EXPLAIN (FORMAT TEXT)
            SELECT p.position_id
            FROM positions AS p
            INNER JOIN exchange_accounts AS a
                ON a.exchange_account_id = p.exchange_account_id
            WHERE a.user_id = @user_id
              AND p.exchange_account_id = @account_id
              AND p.tracking_state IN ('Active', 'Unknown', 'Stale')
            ORDER BY p.first_detected_at DESC, p.position_id DESC
            LIMIT 51;
            """;
        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "user_id";
        userParameter.Value = userId.Value;
        command.Parameters.Add(userParameter);
        var accountParameter = command.CreateParameter();
        accountParameter.ParameterName = "account_id";
        accountParameter.Value = accountId.Value;
        command.Parameters.Add(accountParameter);

        var plans = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plans.Add(reader.GetString(0));
        }

        return plans.ToArray();
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
        DateTimeOffset firstDetectedAt,
        PositionSide positionSide = PositionSide.Long) =>
        Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From(symbol),
                positionSide,
                0),
            MarketCategory.Linear,
            1m,
            firstDetectedAt,
            firstDetectedAt,
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

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
