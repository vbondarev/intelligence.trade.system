using System.Data.Common;
using System.Text.Json;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql-A")]
public sealed class PositionTimelineReadPostgreSqlTests(
    PostgreSqlFixture fixture,
    ITestOutputHelper output)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private const int RepresentativeTimelineRows = 24;
    private static readonly string[] TimelineSourceTables =
        ["position_changes", "position_assessments", "recommendations"];

    [Fact]
    public async Task Timeline_is_owner_scoped_orders_ties_filters_sources_and_pages_bounded_candidates()
    {
        var owner = UserId.New();
        var foreign = UserId.New();
        var positionId = PositionId.New();
        await using (var setup = await CreateMigratedContext())
        {
            await SeedAsync(setup, owner, foreign, positionId);
        }

        var interceptor = new CommandCaptureInterceptor();
        await using var context = await CreateMigratedContext(interceptor);
        var repository = new PositionTimelineReadRepository(context);
        var query = CreateQuery(positionId, 1);

        var candidates = await repository.ReadCandidatesAsync(owner, query);

        Assert.NotNull(candidates);
        Assert.Equal(2, candidates!.PositionChanges.Count);
        Assert.Single(candidates.Evaluations);
        Assert.Single(candidates.Recommendations);
        Assert.Equal(
            [ReasonCode.PortfolioDataStale, ReasonCode.RiskIncreaseBlockedByDataQuality],
            candidates.Evaluations[0].Evaluation!.ReasonCodes);
        Assert.True(candidates.Evaluations[0].Evaluation!.IsLegacy);
        Assert.Equal(
            AssessmentSafetyState.NotEvaluated,
            candidates.Evaluations[0].Evaluation!.DataQuality.SafetyState);
        Assert.Equal(
            [ReasonCode.ProfitProtectionNeeded, ReasonCode.RiskIncreaseBlockedByDataQuality],
            candidates.Recommendations[0].Recommendation!.ReasonCodes);
        Assert.True(candidates.Recommendations[0].Recommendation!.IsLegacy);
        Assert.Equal(1m, candidates.PositionChanges[0].PositionChange!.Before!.Size);
        Assert.Equal(0m, candidates.PositionChanges[0].PositionChange!.After.Size);
        Assert.All(
            TimelineSourceTables,
            table => Assert.Contains(
                interceptor.Commands,
                command =>
                    command.Sql.Contains(table, StringComparison.OrdinalIgnoreCase) &&
                    command.Sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)));

        var service = new PositionTimelineService(repository);
        var first = await service.GetAsync(owner, query);
        var second = await service.GetAsync(
            owner,
            CreateQuery(positionId, 1, first!.NextCursor));
        var third = await service.GetAsync(
            owner,
            CreateQuery(positionId, 1, second!.NextCursor));
        var fourth = await service.GetAsync(
            owner,
            CreateQuery(positionId, 1, third!.NextCursor));

        Assert.Equal(PositionTimelineItemKind.Recommendation, first.Items.Single().Kind);
        Assert.Equal(PositionTimelineItemKind.Evaluation, second.Items.Single().Kind);
        Assert.Equal(PositionTimelineItemKind.PositionChange, third.Items.Single().Kind);
        Assert.Equal(PositionTimelineItemKind.PositionChange, fourth!.Items.Single().Kind);
        Assert.False(fourth.HasMore);
        Assert.Null(fourth.NextCursor);

        var recommendationOnly = await repository.ReadCandidatesAsync(
            owner,
            CreateQuery(positionId, 10, null, [PositionTimelineItemKind.Recommendation]));
        Assert.NotNull(recommendationOnly);
        Assert.Empty(recommendationOnly!.PositionChanges);
        Assert.Empty(recommendationOnly.Evaluations);
        Assert.Single(recommendationOnly.Recommendations);

        Assert.Null(await repository.ReadCandidatesAsync(foreign, query));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.ReadCandidatesAsync(owner, query, cancellation.Token));
    }

    [Fact]
    public async Task Timeline_source_queries_are_owner_scoped_cursor_bounded_and_index_backed()
    {
        var owner = UserId.New();
        var positionId = PositionId.New();
        var cursorAssessmentId = Guid.Parse("10000000-0000-0000-0000-000000000010");
        var cursorRecommendationId = Guid.Parse("20000000-0000-0000-0000-000000000010");

        await using (var setup = await CreateMigratedContext())
        {
            await SeedRepresentativeDatasetAsync(setup, owner, positionId);
            await AnalyzeTimelineTablesAsync(setup);
        }

        var interceptor = new CommandCaptureInterceptor();
        await using var context = await CreateMigratedContext(interceptor);
        var repository = new PositionTimelineReadRepository(context);
        const int pageSize = 2;

        await repository.ReadCandidatesAsync(owner, CreateQuery(positionId, pageSize));
        AssertSourceSql(
            interceptor.Commands,
            "position_changes",
            "occurred_at",
            "sequence",
            pageSize + 1,
            expectsCursor: false);
        AssertSourceSql(
            interceptor.Commands,
            "position_assessments",
            "created_at",
            "position_assessment_id",
            pageSize + 1,
            expectsCursor: false);
        AssertSourceSql(
            interceptor.Commands,
            "recommendations",
            "created_at",
            "recommendation_id",
            pageSize + 1,
            expectsCursor: false);

        interceptor.Commands.Clear();
        await repository.ReadCandidatesAsync(
            owner,
            CreateQuery(
                positionId,
                pageSize,
                new PositionTimelineCursor(
                    T0.AddMinutes(10),
                    PositionTimelineItemKind.Evaluation,
                    cursorAssessmentId,
                    null)));
        AssertCursorSql(
            interceptor.Commands,
            "position_assessments",
            "position_assessment_id",
            pageSize + 1);

        interceptor.Commands.Clear();
        await repository.ReadCandidatesAsync(
            owner,
            CreateQuery(
                positionId,
                pageSize,
                new PositionTimelineCursor(
                    T0.AddMinutes(10),
                    PositionTimelineItemKind.Recommendation,
                    cursorRecommendationId,
                    null)));
        AssertCursorSql(
            interceptor.Commands,
            "recommendations",
            "recommendation_id",
            pageSize + 1);

        interceptor.Commands.Clear();
        await repository.ReadCandidatesAsync(
            owner,
            CreateQuery(
                positionId,
                pageSize,
                new PositionTimelineCursor(
                    T0.AddMinutes(10),
                    PositionTimelineItemKind.PositionChange,
                    null,
                    10)));
        AssertCursorSql(
            interceptor.Commands,
            "position_changes",
            "sequence",
            pageSize + 1);

        var changesPlan = await ExplainTimelineSourceAsync(
            context,
            TimelineSource.PositionChanges,
            owner,
            positionId,
            pageSize + 1);
        var assessmentsPlan = await ExplainTimelineSourceAsync(
            context,
            TimelineSource.Assessments,
            owner,
            positionId,
            pageSize + 1);
        var recommendationsPlan = await ExplainTimelineSourceAsync(
            context,
            TimelineSource.Recommendations,
            owner,
            positionId,
            pageSize + 1);
        var changesWithoutIndex = await ExplainWithoutIndexAsync(
            context,
            "ix_position_changes_position_occurred_at_sequence",
            TimelineSource.PositionChanges,
            owner,
            positionId,
            pageSize + 1);
        var recommendationsWithoutIndex = await ExplainWithoutIndexAsync(
            context,
            "ix_recommendations_position_created_at_id",
            TimelineSource.Recommendations,
            owner,
            positionId,
            pageSize + 1);

        WriteExplainEvidence("position_changes", changesPlan, changesWithoutIndex);
        WriteExplainEvidence("position_assessments", assessmentsPlan);
        WriteExplainEvidence("recommendations", recommendationsPlan, recommendationsWithoutIndex);

        AssertAssessmentPlanIsBounded(
            assessmentsPlan,
            pageSize + 1);
        AssertPlanIsBounded(
            changesPlan,
            "ix_position_changes_position_occurred_at_sequence",
            pageSize + 1);
        AssertPlanIsBounded(
            recommendationsPlan,
            "ix_recommendations_position_created_at_id",
            pageSize + 1);
        Assert.Contains("\"Node Type\": \"Sort\"", changesWithoutIndex);
        Assert.Contains("\"Node Type\": \"Sort\"", recommendationsWithoutIndex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Timeline_cursor_traversal_has_exact_order_without_duplicates_or_gaps(int pageSize)
    {
        var owner = UserId.New();
        var positionId = PositionId.New();
        var ids = TimelineIds.Create();
        await using (var setup = await CreateMigratedContext())
        {
            await SeedTraversalDatasetAsync(setup, owner, positionId, ids);
        }

        var interceptor = new CommandCaptureInterceptor();
        await using var context = await CreateMigratedContext(interceptor);
        var service = new PositionTimelineService(new PositionTimelineReadRepository(context));
        var pages = await ReadAllPagesAsync(service, owner, positionId, pageSize);
        var actual = pages.SelectMany(page => page.Items).Select(GetStableIdentity).ToArray();
        var expected = new[]
        {
            $"R:{ids.R4}", $"R:{ids.R3}", $"A:{ids.A4}", $"A:{ids.A3}", "C:4", "C:3",
            $"R:{ids.R2}", $"A:{ids.A2}", "C:2",
            $"R:{ids.R1}", $"A:{ids.A1}", "C:1",
        };

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, actual.Distinct(StringComparer.Ordinal).Count());
        Assert.All(pages.Take(pages.Count - 1), page => Assert.True(page.HasMore));
        Assert.False(pages[^1].HasMore);
        Assert.Null(pages[^1].NextCursor);

        var cursors = pages
            .Take(pages.Count - 1)
            .Select(page => page.NextCursor!.Value)
            .ToArray();
        Assert.Contains(cursors, cursor => cursor.Kind == PositionTimelineItemKind.Evaluation);
        Assert.Contains(cursors, cursor => cursor.Kind == PositionTimelineItemKind.Recommendation);
        Assert.Contains(cursors, cursor => cursor.Kind == PositionTimelineItemKind.PositionChange);
        Assert.All(
            TimelineSourceTables,
            table => Assert.Contains(
                interceptor.Commands,
                command =>
                    command.Sql.Contains(table, StringComparison.OrdinalIgnoreCase) &&
                    command.Sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Timeline_persists_a1_r1_a2_without_a_synthetic_r2()
    {
        var owner = UserId.New();
        var positionId = PositionId.New();
        var ids = new TimelineIds(
            Guid.Parse("31000000-0000-0000-0000-000000000001"),
            Guid.Parse("31000000-0000-0000-0000-000000000002"),
            Guid.Parse("31000000-0000-0000-0000-000000000003"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        await using (var setup = await CreateMigratedContext())
        {
            var accountId = ExchangeAccountId.New();
            setup.ExchangeAccounts.Add(CreateAccount(accountId, owner, "anti-chatter"));
            setup.Positions.Add(CreatePosition(positionId, accountId));
            setup.PositionAssessments.AddRange(
                CreateAssessment(positionId, accountId, ids.A1, T0),
                CreateAssessment(positionId, accountId, ids.A2, T0.AddMinutes(2)));
            setup.Recommendations.Add(CreateRecommendation(
                positionId,
                ids.A1,
                ids.R1,
                T0.AddMinutes(1)));
            await setup.SaveChangesAsync();
        }

        await using var context = await CreateMigratedContext();
        var page = await new PositionTimelineService(new PositionTimelineReadRepository(context))
            .GetAsync(owner, CreateQuery(positionId, 10));

        Assert.NotNull(page);
        Assert.Equal(
            [$"A:{ids.A2}", $"R:{ids.R1}", $"A:{ids.A1}"],
            page!.Items.Select(GetStableIdentity));
        Assert.DoesNotContain(page.Items, item =>
            item.Kind == PositionTimelineItemKind.Recommendation &&
            item.Recommendation!.Id.Value != ids.R1);
        Assert.Equal(
            1,
            await context.Recommendations.CountAsync(
                recommendation => recommendation.PositionId == positionId.Value));
    }

    private Task<TradeSystemDbContext> CreateMigratedContext(
        DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name));
        if (interceptor is not null)
            options.AddInterceptors(interceptor);

        return Task.FromResult(new TradeSystemDbContext(options.Options));
    }

    private static PositionTimelineQuery CreateQuery(
        PositionId positionId,
        int pageSize,
        PositionTimelineCursor? cursor = null,
        IEnumerable<PositionTimelineItemKind>? kinds = null) =>
        new(
            positionId,
            pageSize,
            kinds ??
            [
                PositionTimelineItemKind.PositionChange,
                PositionTimelineItemKind.Evaluation,
                PositionTimelineItemKind.Recommendation,
            ],
            cursor);

    private static async Task<List<PositionTimelinePage>> ReadAllPagesAsync(
        PositionTimelineService service,
        UserId owner,
        PositionId positionId,
        int pageSize)
    {
        var pages = new List<PositionTimelinePage>();
        PositionTimelineCursor? cursor = null;
        do
        {
            var page = await service.GetAsync(owner, CreateQuery(positionId, pageSize, cursor));
            Assert.NotNull(page);
            pages.Add(page!);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return pages;
    }

    private static void AssertSourceSql(
        IReadOnlyList<CapturedCommand> commands,
        string table,
        string occurredAtColumn,
        string sourceIdentityColumn,
        int limit,
        bool expectsCursor)
    {
        var command = GetSourceCommand(commands, table);
        Assert.Contains("position_id", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exchange_accounts", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user_id", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            command.Parameters.Values,
            value => value is int integer && integer == limit);
        Assert.Equal(expectsCursor, command.Sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase));
        AssertDescendingTimelineOrder(
            command.Sql,
            occurredAtColumn,
            sourceIdentityColumn);
    }

    private static void AssertCursorSql(
        IReadOnlyList<CapturedCommand> commands,
        string table,
        string sourceIdentityColumn,
        int limit)
    {
        var command = GetSourceCommand(commands, table);
        Assert.Contains("position_id", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user_id", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sourceIdentityColumn, command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" OR ", command.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<", command.Sql, StringComparison.Ordinal);
        Assert.Contains(
            command.Parameters.Values,
            value => value is int integer && integer == limit);
    }

    private static CapturedCommand GetSourceCommand(
        IReadOnlyList<CapturedCommand> commands,
        string table) =>
        Assert.Single(
            commands,
            command => command.Sql.Contains($"FROM {table}", StringComparison.OrdinalIgnoreCase));

    private static void AssertDescendingTimelineOrder(
        string sql,
        string temporalColumn,
        string sourceIdentityColumn)
    {
        var orderByStart = sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        var limitStart = sql.IndexOf("LIMIT", orderByStart, StringComparison.OrdinalIgnoreCase);
        Assert.True(orderByStart >= 0 && limitStart > orderByStart);

        var orderBy = sql[orderByStart..limitStart];
        var temporalColumnIndex = orderBy.IndexOf(
            temporalColumn,
            StringComparison.OrdinalIgnoreCase);
        var sourceIdentityColumnIndex = orderBy.IndexOf(
            sourceIdentityColumn,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(temporalColumnIndex >= 0);
        Assert.True(sourceIdentityColumnIndex > temporalColumnIndex);
        Assert.Equal(
            2,
            System.Text.RegularExpressions.Regex.Count(
                orderBy,
                @"\bDESC\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static async Task SeedAsync(
        TradeSystemDbContext context,
        UserId owner,
        UserId foreign,
        PositionId positionId)
    {
        var accountId = ExchangeAccountId.New();
        var foreignAccountId = ExchangeAccountId.New();
        context.ExchangeAccounts.AddRange(
            CreateAccount(accountId, owner, "owner"),
            CreateAccount(foreignAccountId, foreign, "foreign"));
        context.Positions.AddRange(
            CreatePosition(positionId, accountId),
            CreatePosition(PositionId.New(), foreignAccountId));

        var assessmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        context.PositionAssessments.Add(CreateAssessment(positionId, accountId, assessmentId, T0));
        context.PositionAssessmentReasons.AddRange(
            new PositionAssessmentReasonEntity
            {
                PositionAssessmentId = assessmentId,
                Sequence = 2,
                ReasonCode = ReasonCode.RiskIncreaseBlockedByDataQuality,
            },
            new PositionAssessmentReasonEntity
            {
                PositionAssessmentId = assessmentId,
                Sequence = 1,
                ReasonCode = ReasonCode.PortfolioDataStale,
            });

        var recommendationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        context.Recommendations.Add(CreateRecommendation(positionId, assessmentId, recommendationId, T0));
        context.RecommendationReasons.AddRange(
            new RecommendationReasonEntity
            {
                RecommendationId = recommendationId,
                Sequence = 2,
                ReasonCode = ReasonCode.RiskIncreaseBlockedByDataQuality,
            },
            new RecommendationReasonEntity
            {
                RecommendationId = recommendationId,
                Sequence = 1,
                ReasonCode = ReasonCode.ProfitProtectionNeeded,
            });
        context.PositionChanges.AddRange(
            CreateChange(positionId, 1, T0),
            CreateChange(positionId, 2, T0));

        await context.SaveChangesAsync();
    }

    private static async Task SeedRepresentativeDatasetAsync(
        TradeSystemDbContext context,
        UserId owner,
        PositionId positionId)
    {
        var accountId = ExchangeAccountId.New();
        context.ExchangeAccounts.Add(CreateAccount(accountId, owner, "plan-owner"));
        context.Positions.Add(CreatePosition(positionId, accountId));
        for (var index = 1; index <= RepresentativeTimelineRows; index++)
        {
            var occurredAt = T0.AddMinutes(index);
            var assessmentId = index == 10
                ? Guid.Parse("10000000-0000-0000-0000-000000000010")
                : Guid.Parse($"10000000-0000-0000-0000-{index:D12}");
            var recommendationId = index == 10
                ? Guid.Parse("20000000-0000-0000-0000-000000000010")
                : Guid.Parse($"20000000-0000-0000-0000-{index:D12}");
            context.PositionChanges.Add(CreateChange(positionId, index, occurredAt));
            context.PositionAssessments.Add(CreateAssessment(positionId, accountId, assessmentId, occurredAt));
            context.Recommendations.Add(CreateRecommendation(
                positionId,
                assessmentId,
                recommendationId,
                occurredAt));
        }

        for (var index = 1; index <= 1_024; index++)
        {
            var noisePositionId = PositionId.New();
            var assessmentId = Guid.NewGuid();
            context.Positions.Add(CreatePosition(noisePositionId, accountId));
            context.PositionChanges.Add(CreateChange(noisePositionId, 1, T0.AddDays(-index)));
            context.PositionAssessments.Add(CreateAssessment(
                noisePositionId,
                accountId,
                assessmentId,
                T0.AddDays(-index)));
            context.Recommendations.Add(CreateRecommendation(
                noisePositionId,
                assessmentId,
                Guid.NewGuid(),
                T0.AddDays(-index)));
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedTraversalDatasetAsync(
        TradeSystemDbContext context,
        UserId owner,
        PositionId positionId,
        TimelineIds ids)
    {
        var accountId = ExchangeAccountId.New();
        context.ExchangeAccounts.Add(CreateAccount(accountId, owner, "traversal"));
        context.Positions.Add(CreatePosition(positionId, accountId));
        context.PositionChanges.AddRange(
            CreateChange(positionId, 1, T0),
            CreateChange(positionId, 2, T0.AddMinutes(1)),
            CreateChange(positionId, 3, T0.AddMinutes(2)),
            CreateChange(positionId, 4, T0.AddMinutes(2)));
        context.PositionAssessments.AddRange(
            CreateAssessment(positionId, accountId, ids.A1, T0),
            CreateAssessment(positionId, accountId, ids.A2, T0.AddMinutes(1)),
            CreateAssessment(positionId, accountId, ids.A3, T0.AddMinutes(2)),
            CreateAssessment(positionId, accountId, ids.A4, T0.AddMinutes(2)));
        context.Recommendations.AddRange(
            CreateRecommendation(positionId, ids.A1, ids.R1, T0),
            CreateRecommendation(positionId, ids.A2, ids.R2, T0.AddMinutes(1)),
            CreateRecommendation(positionId, ids.A3, ids.R3, T0.AddMinutes(2)),
            CreateRecommendation(positionId, ids.A4, ids.R4, T0.AddMinutes(2)));
        await context.SaveChangesAsync();
    }

    private static async Task AnalyzeTimelineTablesAsync(TradeSystemDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("ANALYZE position_changes;");
        await context.Database.ExecuteSqlRawAsync("ANALYZE position_assessments;");
        await context.Database.ExecuteSqlRawAsync("ANALYZE recommendations;");
    }

    private async Task<string> ExplainTimelineSourceAsync(
        TradeSystemDbContext context,
        TimelineSource source,
        UserId owner,
        PositionId positionId,
        int limit,
        DbTransaction? transaction = null)
    {
        var (table, identity, occurredAt, accountMatch) = source switch
        {
            TimelineSource.PositionChanges =>
                ("position_changes", "sequence", "occurred_at", string.Empty),
            TimelineSource.Assessments =>
                ("position_assessments", "position_assessment_id", "created_at",
                    "AND position.exchange_account_id = source.exchange_account_id"),
            TimelineSource.Recommendations =>
                ("recommendations", "recommendation_id", "created_at", string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
            SELECT source.{identity}
            FROM {table} AS source
            WHERE source.position_id = '{positionId.Value}'
              AND EXISTS (
                  SELECT 1
                  FROM positions AS position
                  WHERE position.position_id = source.position_id
                    {accountMatch}
                    AND EXISTS (
                        SELECT 1
                        FROM exchange_accounts AS account
                        WHERE account.exchange_account_id = position.exchange_account_id
                          AND account.user_id = '{owner.Value}'))
            ORDER BY source.{occurredAt} DESC, source.{identity} DESC
            LIMIT {limit};
            """;
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private async Task<string> ExplainWithoutIndexAsync(
        TradeSystemDbContext context,
        string indexName,
        TimelineSource source,
        UserId owner,
        PositionId positionId,
        int limit)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var drop = connection.CreateCommand())
            {
                drop.Transaction = transaction;
                drop.CommandText = $"DROP INDEX {indexName};";
                await drop.ExecuteNonQueryAsync();
            }

            var explain = await ExplainTimelineSourceAsync(
                context,
                source,
                owner,
                positionId,
                limit,
                transaction);
            await transaction.RollbackAsync();
            return explain;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private void WriteExplainEvidence(string source, string indexedPlan, string? unindexedPlan = null)
    {
        output.WriteLine($"{source} indexed EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON):");
        output.WriteLine(indexedPlan);
        if (unindexedPlan is null)
            return;

        output.WriteLine($"{source} EXPLAIN after transactionally dropping its timeline index:");
        output.WriteLine(unindexedPlan);
    }

    private static void AssertPlanIsBounded(string explain, string indexName, int limit)
    {
        using var document = JsonDocument.Parse(explain);
        var plan = document.RootElement[0].GetProperty("Plan");
        Assert.Equal("Limit", plan.GetProperty("Node Type").GetString());
        Assert.InRange(plan.GetProperty("Actual Rows").GetDouble(), 0, limit);
        Assert.Contains(indexName, explain, StringComparison.Ordinal);
        Assert.Contains("Shared Hit Blocks", explain, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Node Type\": \"Sort\"", explain);
    }

    private static void AssertAssessmentPlanIsBounded(string explain, int limit)
    {
        using var document = JsonDocument.Parse(explain);
        var plan = document.RootElement[0].GetProperty("Plan");
        Assert.Equal("Limit", plan.GetProperty("Node Type").GetString());
        Assert.InRange(plan.GetProperty("Actual Rows").GetDouble(), 0, limit);
        Assert.Contains(
            "ix_position_assessments_position_created_at_id",
            explain,
            StringComparison.Ordinal);
        Assert.Contains("Shared Hit Blocks", explain, StringComparison.Ordinal);
        var sourcePlan = FindRelationPlanNode(plan, "position_assessments");
        Assert.InRange(
            sourcePlan.GetProperty("Actual Rows").GetDouble(),
            0,
            RepresentativeTimelineRows);
    }

    private static JsonElement FindRelationPlanNode(JsonElement plan, string relationName)
    {
        if (plan.TryGetProperty("Relation Name", out var relation) &&
            relation.ValueEquals(relationName))
            return plan;

        if (plan.TryGetProperty("Plans", out var plans))
        {
            foreach (var child in plans.EnumerateArray())
            {
                try
                {
                    return FindRelationPlanNode(child, relationName);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        throw new InvalidOperationException($"Query plan does not contain relation '{relationName}'.");
    }

    private static string GetStableIdentity(PositionTimelineItem item) => item.Kind switch
    {
        PositionTimelineItemKind.PositionChange => $"C:{item.PositionChange!.Sequence}",
        PositionTimelineItemKind.Evaluation => $"A:{item.Evaluation!.Id.Value}",
        PositionTimelineItemKind.Recommendation => $"R:{item.Recommendation!.Id.Value}",
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    private static ExchangeAccountEntity CreateAccount(
        ExchangeAccountId id,
        UserId userId,
        string providerAccountId) =>
        new()
        {
            Id = id.Value,
            UserId = userId.Value,
            ExchangeId = ExchangeId.Bybit,
            ProviderAccountId = providerAccountId,
            ConnectionStatus = ExchangeAccountConnectionStatus.Connected,
            Capabilities = ExchangeAccountCapabilities.ReadPositions,
            Version = 1,
        };

    private static PositionEntity CreatePosition(PositionId id, ExchangeAccountId accountId) =>
        new()
        {
            Id = id.Value,
            ExchangeAccountId = accountId.Value,
            InstrumentId = "BTCUSDT",
            PositionSide = PositionSide.Long,
            PositionIdx = 0,
            MarketCategory = MarketCategory.Linear,
            Size = 1m,
            FirstDetectedAt = T0,
            LastObservedAt = T0,
            ClosedAt = T0,
            TrackingState = PositionTrackingState.Closed,
            Version = 1,
        };

    private static PositionChangeEntity CreateChange(
        PositionId positionId,
        int sequence,
        DateTimeOffset occurredAt) =>
        new()
        {
            PositionId = positionId.Value,
            Sequence = sequence,
            Kind = PositionChangeKind.Closed,
            Cause = PositionChangeCause.ExchangeObservation,
            OccurredAt = occurredAt,
            TrackingStateAfter = PositionTrackingState.Closed,
            BeforeSize = 1m,
            AfterSize = 0m,
        };

    private static PositionAssessmentEntity CreateAssessment(
        PositionId positionId,
        ExchangeAccountId accountId,
        Guid id,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = id,
            PositionId = positionId.Value,
            ExchangeAccountId = accountId.Value,
            InstrumentId = "BTCUSDT",
            PositionObservedAt = createdAt,
            PortfolioCalculatedAt = createdAt,
            MarketCapturedAt = createdAt,
            RuleVersion = "assessment-v1",
            BasePolicyConfigurationVersion = "base-v1",
            BasePolicyConfigurationHash = "base-hash",
            PolicyConfigurationVersion = "policy-v1",
            PolicyConfigurationHash = "policy-hash",
            ResultJson = null,
            CreatedAt = createdAt,
            ValidUntil = createdAt.AddHours(1),
            PortfolioRiskDecision = RiskIncreaseDecision.Blocked,
        };

    private static RecommendationEntity CreateRecommendation(
        PositionId positionId,
        Guid assessmentId,
        Guid recommendationId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = recommendationId,
            AssessmentId = assessmentId,
            PositionId = positionId.Value,
            RecommendedAction = PositionAction.Watch,
            AddDecision = AddDecision.DoNotAdd,
            PolicyVersion = "recommendation-v1",
            CreatedAt = createdAt,
            ValidUntil = createdAt.AddHours(1),
            Status = RecommendationStatus.Superseded,
            Version = 1,
        };

    private enum TimelineSource
    {
        PositionChanges,
        Assessments,
        Recommendations,
    }

    private sealed record TimelineIds(
        Guid A1,
        Guid A2,
        Guid A3,
        Guid A4,
        Guid R1,
        Guid R2,
        Guid R3,
        Guid R4)
    {
        public static TimelineIds Create()
        {
            var value = Guid.NewGuid().ToString("N");
            var prefix = $"{value[..8]}-{value[8..12]}-{value[12..16]}-{value[16..20]}";

            Guid CreateId(int sequence) => Guid.Parse($"{prefix}-{sequence:D12}");

            return new(
                CreateId(1),
                CreateId(2),
                CreateId(3),
                CreateId(4),
                CreateId(5),
                CreateId(6),
                CreateId(7),
                CreateId(8));
        }
    }

    private sealed record CapturedCommand(string Sql, IReadOnlyDictionary<string, object?> Parameters);

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new CapturedCommand(
                command.CommandText,
                command.Parameters
                    .Cast<DbParameter>()
                    .ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value)));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
