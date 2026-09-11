using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class UserScopedRepositoryPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exact_foreign_ids_are_hidden_while_each_owner_can_read_their_aggregates()
    {
        var first = CreateAggregateSet("BTCUSDT");
        var second = CreateAggregateSet("ETHUSDT");

        await Persist(first);
        await Persist(second);

        await using var dbContext = await CreateMigratedContext();
        var accounts = new ExchangeAccountRepository(dbContext);
        var positions = new PositionRepository(dbContext);
        var portfolios = new PortfolioStateRepository(dbContext);
        var assessments = new PositionAssessmentRepository(dbContext);
        var recommendations = new RecommendationRepository(dbContext);

        Assert.NotNull(await accounts.GetByIdAsync(first.Account.UserId, first.Account.Id));
        Assert.NotNull(await positions.GetByIdAsync(first.Account.UserId, first.Position.Id));
        Assert.NotNull(await portfolios.GetLatestAsync(first.Account.UserId, first.Account.Id));
        Assert.NotNull(await assessments.GetByIdAsync(first.Account.UserId, first.Assessment.Id));
        Assert.NotNull(await recommendations.GetByIdAsync(first.Account.UserId, first.Recommendation.Id));

        Assert.NotNull(await accounts.GetByIdAsync(second.Account.UserId, second.Account.Id));
        Assert.NotNull(await positions.GetByIdAsync(second.Account.UserId, second.Position.Id));
        Assert.NotNull(await portfolios.GetLatestAsync(second.Account.UserId, second.Account.Id));
        Assert.NotNull(await assessments.GetByIdAsync(second.Account.UserId, second.Assessment.Id));
        Assert.NotNull(await recommendations.GetByIdAsync(second.Account.UserId, second.Recommendation.Id));

        Assert.Null(await accounts.GetByIdAsync(second.Account.UserId, first.Account.Id));
        Assert.Null(await positions.GetByIdAsync(second.Account.UserId, first.Position.Id));
        Assert.Null(await portfolios.GetLatestAsync(second.Account.UserId, first.Account.Id));
        Assert.Null(await assessments.GetByIdAsync(second.Account.UserId, first.Assessment.Id));
        Assert.Null(await recommendations.GetByIdAsync(second.Account.UserId, first.Recommendation.Id));
    }

    [Fact]
    public async Task Position_account_query_is_scoped_to_user_and_exchange_account_and_returns_versions()
    {
        var userId = UserId.New();
        var firstAccount = CreateAccount(userId);
        var secondAccount = CreateAccount(userId);
        var firstPosition = CreatePosition(firstAccount.Id, "BTCUSDT");
        var secondPosition = CreatePosition(secondAccount.Id, "ETHUSDT");
        var foreignUserId = UserId.New();

        await using (var dbContext = await CreateMigratedContext())
        {
            var accountRepository = new ExchangeAccountRepository(dbContext);
            var positionRepository = new PositionRepository(dbContext);
            await accountRepository.SaveAsync(userId, firstAccount, expectedVersion: null);
            await accountRepository.SaveAsync(userId, secondAccount, expectedVersion: null);
            await positionRepository.SaveAsync(userId, firstPosition, expectedVersion: null);
            await positionRepository.SaveAsync(userId, secondPosition, expectedVersion: null);
        }

        await using var queryContext = await CreateMigratedContext();
        var queryRepository = new PositionRepository(queryContext);
        var firstResult = await queryRepository.GetByExchangeAccountAsync(userId, firstAccount.Id);

        Assert.Single(firstResult);
        Assert.Equal(firstPosition.Id, firstResult.Single().Value.Id);
        Assert.Equal(ConcurrencyVersion.Initial, firstResult.Single().Version);
        var secondResult = await queryRepository.GetByExchangeAccountAsync(userId, secondAccount.Id);
        Assert.Single(secondResult);
        Assert.Equal(secondPosition.Id, secondResult.Single().Value.Id);
        Assert.Empty(await queryRepository.GetByExchangeAccountAsync(foreignUserId, firstAccount.Id));
    }

    [Fact]
    public async Task Foreign_versioned_writes_do_not_change_rows_versions_history_or_reasons()
    {
        var owner = CreateAggregateSet("SOLUSDT");
        var foreignUserId = UserId.New();
        await Persist(owner);

        var changedAccount = ExchangeAccount.Create(
            owner.Account.Id,
            owner.Account.UserId,
            owner.Account.ExchangeId,
            ExchangeAccountConnectionStatus.Disabled,
            owner.Account.Capabilities,
            T0.AddMinutes(10),
            "foreign update");
        owner.Position.ApplyObservation(
            2m,
            T0.AddMinutes(10),
            averageEntryPrice: 110m,
            positionValue: 220m,
            leverage: 2m);
        owner.Recommendation.Acknowledge(T0.AddMinutes(5));

        await using (var context = await CreateMigratedContext())
            await AssertForeignWriteRejected(
                () => new ExchangeAccountRepository(context)
                    .SaveAsync(foreignUserId, changedAccount, ConcurrencyVersion.Initial));
        await using (var context = await CreateMigratedContext())
            await AssertForeignWriteRejected(
                () => new PositionRepository(context)
                    .SaveAsync(foreignUserId, owner.Position, ConcurrencyVersion.Initial));
        await using (var context = await CreateMigratedContext())
            await AssertForeignWriteRejected(
                () => new RecommendationRepository(context)
                    .SaveAsync(foreignUserId, owner.Recommendation, ConcurrencyVersion.Initial));

        await using var verificationContext = await CreateMigratedContext();
        var accountRow = await verificationContext.ExchangeAccounts
            .SingleAsync(row => row.Id == owner.Account.Id.Value);
        var positionRow = await verificationContext.Positions
            .SingleAsync(row => row.Id == owner.Position.Id.Value);
        var recommendationRow = await verificationContext.Recommendations
            .SingleAsync(row => row.Id == owner.Recommendation.Id.Value);

        Assert.Equal(1L, accountRow.Version);
        Assert.Null(accountRow.LastError);
        Assert.Equal(1L, positionRow.Version);
        Assert.Equal(1m, positionRow.Size);
        Assert.Single(await verificationContext.PositionChanges
            .Where(row => row.PositionId == owner.Position.Id.Value)
            .ToArrayAsync());
        Assert.Equal(1L, recommendationRow.Version);
        Assert.Equal(RecommendationStatus.Active, recommendationRow.Status);
        Assert.Equal(owner.Recommendation.ReasonCodes, await verificationContext.RecommendationReasons
            .Where(row => row.RecommendationId == owner.Recommendation.Id.Value)
            .OrderBy(row => row.Sequence)
            .Select(row => row.ReasonCode)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Foreign_nonversioned_writes_do_not_append_portfolios_or_mutate_assessments()
    {
        var owner = CreateAggregateSet("XRPUSDT");
        var foreignUserId = UserId.New();
        await Persist(owner);

        var foreignPortfolio = PortfolioState.Create(
            owner.Account.Id,
            [owner.Position],
            new PortfolioCapitalState(900m, 500m, T0.AddMinutes(9), 900m),
            T0.AddMinutes(10),
            TimeSpan.FromMinutes(5));
        var foreignAssessment = PositionAssessment.Restore(
            owner.Assessment.Id,
            owner.Assessment.InputVersions,
            new RuleVersion("foreign-rule"),
            owner.Assessment.CreatedAt,
            owner.Assessment.ValidUntil,
            owner.Assessment.PortfolioRiskDecision,
            owner.Assessment.ReasonCodes);

        await using (var context = await CreateMigratedContext())
            await AssertForeignWriteRejected(
                () => new PortfolioStateRepository(context).SaveAsync(foreignUserId, foreignPortfolio));
        await using (var context = await CreateMigratedContext())
            await AssertForeignWriteRejected(
                () => new PositionAssessmentRepository(context).SaveAsync(foreignUserId, foreignAssessment));

        await using var verificationContext = await CreateMigratedContext();
        Assert.Single(await verificationContext.PortfolioStates
            .Where(row => row.ExchangeAccountId == owner.Account.Id.Value)
            .ToArrayAsync());
        var assessmentRow = await verificationContext.PositionAssessments
            .SingleAsync(row => row.Id == owner.Assessment.Id.Value);
        Assert.Equal(owner.Assessment.RuleVersion.Value, assessmentRow.RuleVersion);
        Assert.Equal(owner.Assessment.ReasonCodes, await verificationContext.PositionAssessmentReasons
            .Where(row => row.PositionAssessmentId == owner.Assessment.Id.Value)
            .OrderBy(row => row.Sequence)
            .Select(row => row.ReasonCode)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Portfolio_state_rejects_a_foreign_position_reference_and_creates_no_rows()
    {
        var owner = CreateAggregateSet("BTCUSDT");
        var foreign = CreateAggregateSet("ETHUSDT");

        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Account, expectedVersion: null);
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(foreign.Account.UserId, foreign.Account, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Position, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(foreign.Account.UserId, foreign.Position, expectedVersion: null);

        var crafted = PortfolioState.Restore(
            owner.Account.Id,
            [CreatePortfolioPositionState(foreign.Position, owner.Account.Id)],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        await AssertForeignWriteRejected(
            () => new PortfolioStateRepository(dbContext)
                .SaveAsync(owner.Account.UserId, crafted));

        Assert.False(await dbContext.PortfolioStates
            .AnyAsync(state => state.ExchangeAccountId == owner.Account.Id.Value));
        Assert.False(await dbContext.PortfolioPositionStates
            .AnyAsync(state =>
                state.PositionId == foreign.Position.Id.Value &&
                state.ExchangeAccountId == owner.Account.Id.Value));
    }

    [Fact]
    public async Task Portfolio_state_rejects_duplicate_position_ids_before_persistence()
    {
        var owner = CreateAggregateSet("BTCUSDT");

        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Account, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Position, expectedVersion: null);

        var snapshot = CreatePortfolioPositionState(owner.Position, owner.Account.Id);
        var crafted = PortfolioState.Restore(
            owner.Account.Id,
            [snapshot, snapshot],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        await AssertForeignWriteRejected(
            () => new PortfolioStateRepository(dbContext)
                .SaveAsync(owner.Account.UserId, crafted));

        Assert.False(await dbContext.PortfolioStates
            .AnyAsync(state => state.ExchangeAccountId == owner.Account.Id.Value));
    }

    [Fact]
    public async Task Tracked_foreign_position_assessment_cannot_bypass_user_scope()
    {
        var owner = CreateAggregateSet("BTCUSDT");
        var foreign = CreateAggregateSet("ETHUSDT");

        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Account, expectedVersion: null);
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(foreign.Account.UserId, foreign.Account, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Position, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(foreign.Account.UserId, foreign.Position, expectedVersion: null);

        var repository = new PositionAssessmentRepository(dbContext);
        await repository.SaveAsync(owner.Account.UserId, owner.Assessment);
        Assert.Contains(
            dbContext.ChangeTracker.Entries<PositionAssessmentEntity>(),
            entry => entry.Entity.Id == owner.Assessment.Id.Value);

        var crafted = PositionAssessment.Restore(
            owner.Assessment.Id,
            new PositionAssessmentInputVersions(
                foreign.Position.Id,
                foreign.Account.Id,
                foreign.Position.ExchangePositionKey.InstrumentId,
                T0,
                T0.AddMinutes(1),
                T0.AddMinutes(2)),
            new RuleVersion("foreign-rule"),
            owner.Assessment.CreatedAt,
            owner.Assessment.ValidUntil,
            owner.Assessment.PortfolioRiskDecision,
            owner.Assessment.ReasonCodes);

        await AssertForeignWriteRejected(
            () => repository.SaveAsync(foreign.Account.UserId, crafted));

        await using var verificationContext = await CreateMigratedContext();
        var persisted = await verificationContext.PositionAssessments
            .SingleAsync(assessment => assessment.Id == owner.Assessment.Id.Value);
        Assert.Equal(owner.Assessment.PositionId.Value, persisted.PositionId);
        Assert.Equal(owner.Assessment.InputVersions.ExchangeAccountId.Value, persisted.ExchangeAccountId);
        Assert.Equal(owner.Assessment.RuleVersion.Value, persisted.RuleVersion);
        Assert.Equal(owner.Assessment.ReasonCodes, await verificationContext.PositionAssessmentReasons
            .Where(reason => reason.PositionAssessmentId == owner.Assessment.Id.Value)
            .OrderBy(reason => reason.Sequence)
            .Select(reason => reason.ReasonCode)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Position_assessment_cannot_be_reparented_to_another_position_of_the_same_user()
    {
        var owner = CreateAggregateSet("BTCUSDT");
        var secondPosition = Position.Create(
            ExchangePositionKey.Create(
                owner.Account.Id,
                InstrumentId.From("ETHUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);

        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Account, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(owner.Account.UserId, owner.Position, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(owner.Account.UserId, secondPosition, expectedVersion: null);

        var repository = new PositionAssessmentRepository(dbContext);
        await repository.SaveAsync(owner.Account.UserId, owner.Assessment);

        var crafted = PositionAssessment.Restore(
            owner.Assessment.Id,
            new PositionAssessmentInputVersions(
                secondPosition.Id,
                owner.Account.Id,
                secondPosition.ExchangePositionKey.InstrumentId,
                T0,
                T0.AddMinutes(1),
                T0.AddMinutes(2)),
            new RuleVersion("reparented-rule"),
            owner.Assessment.CreatedAt,
            owner.Assessment.ValidUntil,
            owner.Assessment.PortfolioRiskDecision,
            owner.Assessment.ReasonCodes);

        await AssertForeignWriteRejected(
            () => repository.SaveAsync(owner.Account.UserId, crafted));

        await using var verificationContext = await CreateMigratedContext();
        var persisted = await verificationContext.PositionAssessments
            .SingleAsync(assessment => assessment.Id == owner.Assessment.Id.Value);
        Assert.Equal(owner.Assessment.PositionId.Value, persisted.PositionId);
        Assert.Equal(owner.Assessment.InputVersions.ExchangeAccountId.Value, persisted.ExchangeAccountId);
        Assert.Equal(owner.Assessment.RuleVersion.Value, persisted.RuleVersion);
        Assert.Equal(owner.Assessment.ReasonCodes, await verificationContext.PositionAssessmentReasons
            .Where(reason => reason.PositionAssessmentId == owner.Assessment.Id.Value)
            .OrderBy(reason => reason.Sequence)
            .Select(reason => reason.ReasonCode)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Foreign_account_creation_with_another_owner_is_rejected()
    {
        var account = CreateAccount(UserId.New());
        var foreignUserId = UserId.New();

        await using var dbContext = await CreateMigratedContext();
        await AssertForeignWriteRejected(
            () => new ExchangeAccountRepository(dbContext)
                .SaveAsync(foreignUserId, account, expectedVersion: null));

        Assert.False(await dbContext.ExchangeAccounts.AnyAsync(row => row.Id == account.Id.Value));
    }

    [Fact]
    public async Task Exchange_account_user_id_is_required_and_indexed_in_model_and_database()
    {
        await using var dbContext = await CreateMigratedContext();
        var entityType = dbContext.Model.FindEntityType(typeof(ExchangeAccountEntity));
        Assert.NotNull(entityType);
        var userId = entityType!.FindProperty(nameof(ExchangeAccountEntity.UserId));
        Assert.NotNull(userId);
        Assert.False(userId!.IsNullable);
        Assert.Contains(entityType.GetIndexes(), index => index.Properties.Contains(userId));

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_index AS i
                JOIN pg_class AS t ON t.oid = i.indrelid
                JOIN pg_namespace AS n ON n.oid = t.relnamespace
                JOIN pg_attribute AS a
                    ON a.attrelid = t.oid AND a.attnum = ANY(i.indkey)
                WHERE n.nspname = 'public'
                  AND t.relname = 'exchange_accounts'
                  AND a.attname = 'user_id'
                  AND i.indisvalid)
            """;

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    private async Task Persist(AggregateSet values)
    {
        await using var dbContext = await CreateMigratedContext();
        var userId = values.Account.UserId;
        await new ExchangeAccountRepository(dbContext)
            .SaveAsync(userId, values.Account, expectedVersion: null);
        await new PositionRepository(dbContext)
            .SaveAsync(userId, values.Position, expectedVersion: null);
        await new PortfolioStateRepository(dbContext).SaveAsync(userId, values.Portfolio);
        await new PositionAssessmentRepository(dbContext).SaveAsync(userId, values.Assessment);
        await new RecommendationRepository(dbContext)
            .SaveAsync(userId, values.Recommendation, expectedVersion: null);
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static AggregateSet CreateAggregateSet(string instrument)
    {
        var account = CreateAccount(UserId.New());
        var position = Position.Create(
            ExchangePositionKey.Create(
                account.Id,
                InstrumentId.From(instrument),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var assessment = PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                position.Id,
                account.Id,
                position.ExchangePositionKey.InstrumentId,
                T0,
                T0.AddMinutes(1),
                T0.AddMinutes(2)),
            new RuleVersion("assessment-v1"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            T0.AddMinutes(3),
            T0.AddHours(1));
        var recommendation = Recommendation.Restore(
            RecommendationId.New(),
            assessment,
            PositionAction.Reduce,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            assessment.ReasonCodes,
            T0.AddMinutes(4),
            T0.AddMinutes(30),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);

        return new AggregateSet(account, position, portfolio, assessment, recommendation);
    }

    private static ExchangeAccount CreateAccount(UserId userId) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            T0,
            lastError: null);

    private static Position CreatePosition(
        ExchangeAccountId exchangeAccountId,
        string instrument) =>
        Position.Create(
            ExchangePositionKey.Create(
                exchangeAccountId,
                InstrumentId.From(instrument),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);

    private static PortfolioPositionState CreatePortfolioPositionState(
        Position position,
        ExchangeAccountId exchangeAccountId) =>
        new(
            position.Id,
            ExchangePositionKey.Create(
                exchangeAccountId,
                position.ExchangePositionKey.InstrumentId,
                position.ExchangePositionKey.PositionSide,
                position.ExchangePositionKey.PositionIdx),
            position.MarketCategory,
            position.ExchangePositionKey.PositionSide,
            position.TrackingState,
            position.Size,
            position.PositionValue,
            position.UnrealizedPnl,
            position.AverageEntryPrice,
            position.MarkPrice,
            position.LiquidationPrice,
            position.Leverage,
            position.LastObservedAt);

    private static async Task AssertForeignWriteRejected(Func<Task> write)
    {
        var exception = await Record.ExceptionAsync(write);

        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or ConcurrencyConflictException,
            $"Unexpected exception type: {exception.GetType().FullName}");
    }

    private sealed record AggregateSet(
        ExchangeAccount Account,
        Position Position,
        PortfolioState Portfolio,
        PositionAssessment Assessment,
        Recommendation Recommendation);
}
