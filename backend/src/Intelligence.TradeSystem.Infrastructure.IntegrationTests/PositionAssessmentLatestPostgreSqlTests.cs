using System.Data.Common;
using System.Text.Json;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class PositionAssessmentLatestPostgreSqlTests(
    PostgreSqlFixture fixture,
    ITestOutputHelper output)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Latest_assessment_is_deterministic_and_bounded_by_created_at_then_id()
    {
        var account = CreateAccount(UserId.New());
        var position = CreatePosition(account.Id);
        var first = CreateAssessment(account, position, T0.AddMinutes(1));
        var second = CreateAssessment(account, position, T0.AddMinutes(2));
        var tied = CreateAssessment(account, position, T0.AddMinutes(2));

        await using (var setup = await CreateMigratedContext())
        {
            await SaveAccountAndPosition(setup, account, position);
            var repository = new PositionAssessmentRepository(setup);
            await repository.SaveAsync(account.UserId, first);
            await repository.SaveAsync(account.UserId, second);
            await repository.SaveAsync(account.UserId, tied);
        }

        await using var queryContext = await CreateMigratedContext();
        var queryRepository = new PositionAssessmentRepository(queryContext);
        var latest = await queryRepository.GetLatestForPositionAsync(
            account.UserId,
            position.Id);
        var expectedId = await queryContext.PositionAssessments
            .AsNoTracking()
            .Where(row => row.PositionId == position.Id.Value)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Select(row => row.Id)
            .FirstAsync();

        Assert.NotNull(latest);
        Assert.Equal(expectedId, latest!.Id.Value);
        Assert.Equal(tied.CreatedAt, latest.CreatedAt);
    }

    [Fact]
    public async Task Latest_assessment_is_user_scoped()
    {
        var owner = CreateAggregate(UserId.New(), "BTCUSDT");
        var foreign = CreateAggregate(UserId.New(), "ETHUSDT");

        await using (var setup = await CreateMigratedContext())
        {
            await SaveAggregate(setup, owner);
            await SaveAggregate(setup, foreign);
        }

        await using var queryContext = await CreateMigratedContext();
        var repository = new PositionAssessmentRepository(queryContext);

        Assert.NotNull(await repository.GetLatestForPositionAsync(
            owner.Account.UserId,
            owner.Position.Id));
        Assert.Null(await repository.GetLatestForPositionAsync(
            foreign.Account.UserId,
            owner.Position.Id));
    }

    [Fact]
    public async Task Latest_assessment_remains_readable_for_closed_positions()
    {
        var account = CreateAccount(UserId.New());
        var position = CreatePosition(account.Id);
        position.Close(T0.AddMinutes(1));
        var assessment = CreateAssessment(account, position, T0.AddMinutes(2));

        await using (var setup = await CreateMigratedContext())
        {
            await SaveAccountAndPosition(setup, account, position);
            await new PositionAssessmentRepository(setup)
                .SaveAsync(account.UserId, assessment);
        }

        await using var queryContext = await CreateMigratedContext();
        var latest = await new PositionAssessmentRepository(queryContext)
            .GetLatestForPositionAsync(account.UserId, position.Id);

        Assert.NotNull(latest);
        Assert.Equal(assessment.Id, latest!.Id);
    }

    [Fact]
    public async Task Latest_assessment_and_current_recommendation_keep_independent_lifecycle_ids()
    {
        var account = CreateAccount(UserId.New());
        var position = CreatePosition(account.Id);
        var assessmentA1 = CreateAssessment(account, position, T0.AddMinutes(1));
        var assessmentA2 = CreateAssessment(account, position, T0.AddMinutes(2));
        var recommendationR1 = Recommendation.Create(
            assessmentA1,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            [],
            T0.AddMinutes(3),
            T0.AddMinutes(4));

        await using (var setup = await CreateMigratedContext())
        {
            await SaveAccountAndPosition(setup, account, position);
            var assessments = new PositionAssessmentRepository(setup);
            await assessments.SaveAsync(account.UserId, assessmentA1);
            await assessments.SaveAsync(account.UserId, assessmentA2);
            await new RecommendationRepository(setup)
                .SaveAsync(account.UserId, recommendationR1, expectedVersion: null);
        }

        await using var queryContext = await CreateMigratedContext();
        var latest = await new PositionAssessmentRepository(queryContext)
            .GetLatestForPositionAsync(account.UserId, position.Id);
        var current = await new RecommendationRepository(queryContext)
            .GetCurrentForPositionAsync(account.UserId, position.Id);

        Assert.NotNull(latest);
        Assert.NotNull(current);
        Assert.Equal(assessmentA2.Id, latest!.Id);
        Assert.Equal(assessmentA1.Id, current!.Value.AssessmentId);
        Assert.NotEqual(latest.Id, current.Value.AssessmentId);
    }

    [Fact]
    public async Task Latest_assessment_query_is_bounded_and_explain_evidence_is_recorded()
    {
        var account = CreateAccount(UserId.New());
        var position = CreatePosition(account.Id);
        var interceptor = new CommandCaptureInterceptor();

        await using (var setup = await CreateMigratedContext(interceptor))
        {
            await SaveAccountAndPosition(setup, account, position);
            var assessments = new PositionAssessmentRepository(setup);
            for (var index = 0; index < 12; index++)
            {
                await assessments.SaveAsync(
                    account.UserId,
                    CreateAssessment(account, position, T0.AddMinutes(index + 1)));
            }
        }

        interceptor.Commands.Clear();
        await using var queryContext = await CreateMigratedContext(interceptor);
        var latest = await new PositionAssessmentRepository(queryContext)
            .GetLatestForPositionAsync(account.UserId, position.Id);
        var generatedSql = interceptor.Commands
            .First(command =>
                command.Contains("position_assessments", StringComparison.OrdinalIgnoreCase) &&
                command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(latest);
        Assert.Contains("ORDER BY", generatedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT 1", generatedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("position_id", generatedSql, StringComparison.OrdinalIgnoreCase);

        var indexDefinition = await ReadScalarAsync(
            queryContext,
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE tablename = 'position_assessments'
              AND indexdef ILIKE '%position_id%';
            """);
        Assert.Contains("position_id", indexDefinition, StringComparison.OrdinalIgnoreCase);

        var explain = await ExplainAsync(queryContext, account, position);
        output.WriteLine("Generated SQL:");
        output.WriteLine(generatedSql);
        output.WriteLine("Existing position index:");
        output.WriteLine(indexDefinition);
        output.WriteLine("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON):");
        output.WriteLine(explain);
        using var explainDocument = JsonDocument.Parse(explain);
        Assert.True(explainDocument.RootElement.ValueKind == JsonValueKind.Array);
        Assert.Contains("ix_position_assessments_position_created_at_id", explain);
        Assert.DoesNotContain("\"Node Type\": \"Sort\"", explain);
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext(
        DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name));
        if (interceptor is not null)
            options.AddInterceptors(interceptor);

        var context = new TradeSystemDbContext(options.Options);
        await context.Database.MigrateAsync();
        return context;
    }

    private static async Task SaveAccountAndPosition(
        TradeSystemDbContext context,
        ExchangeAccount account,
        Position position)
    {
        await new ExchangeAccountRepository(context)
            .SaveAsync(account.UserId, account, expectedVersion: null);
        await new PositionRepository(context)
            .SaveAsync(account.UserId, position, expectedVersion: null);
    }

    private static async Task SaveAggregate(
        TradeSystemDbContext context,
        Aggregate aggregate)
    {
        await SaveAccountAndPosition(context, aggregate.Account, aggregate.Position);
        await new PositionAssessmentRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Assessment);
    }

    private static async Task<string> ExplainAsync(
        TradeSystemDbContext context,
        ExchangeAccount account,
        Position position)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
            SELECT assessment.position_assessment_id
            FROM position_assessments AS assessment
            WHERE assessment.position_id = '{position.Id.Value}'
              AND EXISTS (
                  SELECT 1
                  FROM positions AS position
                  WHERE position.position_id = assessment.position_id
                    AND position.exchange_account_id = assessment.exchange_account_id
                    AND EXISTS (
                        SELECT 1
                        FROM exchange_accounts AS account
                        WHERE account.exchange_account_id = position.exchange_account_id
                          AND account.user_id = '{account.UserId.Value}'))
            ORDER BY assessment.created_at DESC,
                     assessment.position_assessment_id DESC
            LIMIT 1;
            """;
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static async Task<string> ReadScalarAsync(
        TradeSystemDbContext context,
        string sql)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static ExchangeAccount CreateAccount(UserId userId) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static Position CreatePosition(ExchangeAccountId accountId, string symbol = "BTCUSDT") =>
        Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From(symbol),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0);

    private static PositionAssessment CreateAssessment(
        ExchangeAccount account,
        Position position,
        DateTimeOffset createdAt) =>
        PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                position.Id,
                account.Id,
                position.ExchangePositionKey.InstrumentId,
                T0,
                T0,
                T0),
            new RuleVersion("assessment-v1"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            createdAt,
            createdAt.AddHours(1));

    private sealed record Aggregate(
        ExchangeAccount Account,
        Position Position,
        PositionAssessment Assessment);

    private static Aggregate CreateAggregate(UserId userId, string symbol)
    {
        var account = CreateAccount(userId);
        var position = CreatePosition(account.Id, symbol);
        return new(account, position, CreateAssessment(account, position, T0.AddMinutes(1)));
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
