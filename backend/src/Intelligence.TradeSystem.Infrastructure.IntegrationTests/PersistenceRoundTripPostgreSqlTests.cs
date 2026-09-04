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
using Npgsql;
using System.Data.Common;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class PersistenceRoundTripPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DbContext_connects_to_the_PostgreSql_container()
    {
        await using var dbContext = await CreateMigratedContext();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
        Assert.True(await dbContext.Database.CanConnectAsync());
    }

    [Fact]
    public async Task ExchangeAccount_round_trips_through_a_new_context()
    {
        var account = CreateAccount();

        await using (var dbContext = await CreateMigratedContext())
        {
            var repository = new ExchangeAccountRepository(dbContext);
            var version = await repository.SaveAsync(account, expectedVersion: null);
            Assert.Equal(ConcurrencyVersion.Initial, version);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var reloaded = await new ExchangeAccountRepository(reloadedContext).GetByIdAsync(account.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(ConcurrencyVersion.Initial, reloaded!.Version);
        var reloadedAccount = reloaded.Value;
        Assert.Equal(account.Id, reloadedAccount.Id);
        Assert.Equal(account.UserId, reloadedAccount.UserId);
        Assert.Equal(account.ExchangeId, reloadedAccount.ExchangeId);
        Assert.Equal(account.ConnectionStatus, reloadedAccount.ConnectionStatus);
        Assert.Equal(account.Capabilities, reloadedAccount.Capabilities);
        Assert.Equal(account.LastSyncedAt, reloadedAccount.LastSyncedAt);
        Assert.Equal(account.LastError, reloadedAccount.LastError);
    }

    [Fact]
    public async Task Position_round_trips_with_append_only_history_and_exact_decimals()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            var repository = new PositionRepository(dbContext);
            var v1 = await repository.SaveAsync(position, expectedVersion: null);

            position.ApplyObservation(
                2.123456789012345678m,
                T0.AddMinutes(1),
                averageEntryPrice: 101.123456789012345678m,
                positionValue: 2469.135802469135802468m,
                leverage: 3.25m,
                markPrice: 102.123456789012345678m,
                unrealizedPnl: -12.345678901234567890m);
            position.MarkUnknown(T0.AddMinutes(2));
            position.ApplyObservation(
                1.5m,
                T0.AddMinutes(3),
                averageEntryPrice: 100.5m,
                positionValue: 1800.000000000000000001m,
                leverage: 3m,
                markPrice: 99.5m,
                unrealizedPnl: -0.000000000000000001m);
            position.Close(T0.AddMinutes(4));
            var v2 = await repository.SaveAsync(position, v1);
            Assert.Equal(v1.Next(), v2);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var reloaded = await new PositionRepository(reloadedContext).GetByIdAsync(position.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(new ConcurrencyVersion(2), reloaded!.Version);
        var reloadedPosition = reloaded.Value;
        Assert.Equal(position.Id, reloadedPosition.Id);
        Assert.Equal(position.ExchangePositionKey, reloadedPosition.ExchangePositionKey);
        Assert.Equal(position.MarketCategory, reloadedPosition.MarketCategory);
        Assert.Equal(position.Size, reloadedPosition.Size);
        Assert.Equal(position.AverageEntryPrice, reloadedPosition.AverageEntryPrice);
        Assert.Equal(position.PositionValue, reloadedPosition.PositionValue);
        Assert.Equal(position.Leverage, reloadedPosition.Leverage);
        Assert.Equal(position.MarkPrice, reloadedPosition.MarkPrice);
        Assert.Equal(position.BreakEvenPrice, reloadedPosition.BreakEvenPrice);
        Assert.Equal(position.LiquidationPrice, reloadedPosition.LiquidationPrice);
        Assert.Equal(position.UnrealizedPnl, reloadedPosition.UnrealizedPnl);
        Assert.Equal(position.TakeProfit, reloadedPosition.TakeProfit);
        Assert.Equal(position.StopLoss, reloadedPosition.StopLoss);
        Assert.Equal(position.TrailingStop, reloadedPosition.TrailingStop);
        Assert.Equal(position.FirstDetectedAt, reloadedPosition.FirstDetectedAt);
        Assert.Equal(position.LastObservedAt, reloadedPosition.LastObservedAt);
        Assert.Equal(position.ClosedAt, reloadedPosition.ClosedAt);
        Assert.Equal(position.TrackingState, reloadedPosition.TrackingState);
        Assert.Equal(position.Changes.ToArray(), reloadedPosition.Changes.ToArray());

        var persistedHistory = await reloadedContext.PositionChanges
            .Where(change => change.PositionId == position.Id.Value)
            .OrderBy(change => change.Sequence)
            .ToArrayAsync();
        Assert.Equal(position.Changes.Count, persistedHistory.Length);
        Assert.Equal(Enumerable.Range(1, position.Changes.Count), persistedHistory.Select(change => change.Sequence));
    }

    [Fact]
    public async Task Dynamic_observation_updates_current_position_without_creating_history()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);
        var historyCount = position.Changes.Count;

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            var repository = new PositionRepository(dbContext);
            var v1 = await repository.SaveAsync(position, expectedVersion: null);

            var change = position.ApplyObservation(
                position.Size,
                T0.AddMinutes(1),
                averageEntryPrice: position.AverageEntryPrice,
                positionValue: 2000.000000000000000001m,
                leverage: position.Leverage,
                markPrice: 222.222222222222222222m,
                breakEvenPrice: position.BreakEvenPrice,
                liquidationPrice: position.LiquidationPrice,
                unrealizedPnl: -7.777777777777777777m,
                takeProfit: position.TakeProfit,
                stopLoss: position.StopLoss,
                trailingStop: position.TrailingStop);

            Assert.Null(change);
            Assert.Equal(historyCount, position.Changes.Count);
            var v2 = await repository.SaveAsync(position, v1);

            // A dynamic-only observation still bumps the row version (the row itself
            // changed) even though no PositionChange history row was appended.
            Assert.Equal(v1.Next(), v2);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var reloaded = await new PositionRepository(reloadedContext).GetByIdAsync(position.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(new ConcurrencyVersion(2), reloaded!.Version);
        var reloadedPosition = reloaded.Value;
        Assert.Equal(position.Id, reloadedPosition.Id);
        Assert.Equal(2000.000000000000000001m, reloadedPosition.PositionValue);
        Assert.Equal(222.222222222222222222m, reloadedPosition.MarkPrice);
        Assert.Equal(-7.777777777777777777m, reloadedPosition.UnrealizedPnl);
        Assert.Equal(position.AverageEntryPrice, reloadedPosition.AverageEntryPrice);
        Assert.Equal(position.Leverage, reloadedPosition.Leverage);
        Assert.Equal(position.TrackingState, reloadedPosition.TrackingState);
        Assert.Equal(historyCount, reloadedPosition.Changes.Count);
        Assert.Equal(position.Changes.ToArray(), reloadedPosition.Changes.ToArray());
    }

    [Fact]
    public async Task Sequential_position_saves_increment_the_version_by_one()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
        var repository = new PositionRepository(dbContext);

        var version = await repository.SaveAsync(position, expectedVersion: null);
        Assert.Equal(new ConcurrencyVersion(1), version);

        position.ApplyObservation(
            2m,
            T0.AddMinutes(1),
            averageEntryPrice: 105m,
            leverage: position.Leverage);
        version = await repository.SaveAsync(position, version);
        Assert.Equal(new ConcurrencyVersion(2), version);

        position.ApplyObservation(
            3m,
            T0.AddMinutes(2),
            averageEntryPrice: 110m,
            leverage: position.Leverage);
        version = await repository.SaveAsync(position, version);
        Assert.Equal(new ConcurrencyVersion(3), version);

        var reloaded = await repository.GetByIdAsync(position.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(new ConcurrencyVersion(3), reloaded!.Version);
        Assert.Equal(3, reloaded.Value.Changes.Count);
    }

    [Fact]
    public async Task SubMicrosecond_position_timestamps_are_canonical_and_save_is_repeatable()
    {
        var account = CreateAccount();
        var timestamp = T0.AddTicks(7);
        var position = Position.Create(
            ExchangePositionKey.Create(account.Id, InstrumentId.From("BTCUSDT"), PositionSide.Long, 1),
            MarketCategory.Linear,
            1m,
            timestamp,
            timestamp,
            averageEntryPrice: 100m,
            leverage: 2m);

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            var repository = new PositionRepository(dbContext);
            var v1 = await repository.SaveAsync(position, expectedVersion: null);

            var observedAt = timestamp.AddMinutes(1);
            var change = position.ApplyObservation(
                position.Size,
                observedAt,
                averageEntryPrice: 101m,
                leverage: position.Leverage);
            Assert.NotNull(change);
            await repository.SaveAsync(position, v1);

            var persistedHistory = await dbContext.PositionChanges
                .Where(item => item.PositionId == position.Id.Value)
                .OrderBy(item => item.Sequence)
                .ToArrayAsync();
            Assert.Equal(CanonicalTimestamp(timestamp), persistedHistory[0].OccurredAt);
            Assert.Equal(CanonicalTimestamp(observedAt), persistedHistory[1].OccurredAt);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var reloaded = await new PositionRepository(reloadedContext).GetByIdAsync(position.Id);

        Assert.NotNull(reloaded);
        var reloadedPosition = reloaded!.Value;
        Assert.Equal(2, reloadedPosition.Changes.Count);
        Assert.Equal(CanonicalTimestamp(timestamp), reloadedPosition.Changes[0].OccurredAt);
        Assert.Equal(CanonicalTimestamp(timestamp.AddMinutes(1)), reloadedPosition.Changes[1].OccurredAt);
        Assert.Equal(CanonicalTimestamp(timestamp), reloadedPosition.FirstDetectedAt);
        Assert.Equal(CanonicalTimestamp(timestamp.AddMinutes(1)), reloadedPosition.LastObservedAt);
    }

    [Fact]
    public async Task Closed_lifecycle_can_be_followed_by_a_new_lifecycle_with_the_same_exchange_key()
    {
        var account = CreateAccount();
        var first = CreatePosition(account.Id);

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(dbContext).SaveAsync(first, expectedVersion: null);
        }

        await using (var dbContext = await CreateMigratedContext())
        {
            var repository = new PositionRepository(dbContext);
            var loaded = await repository.GetByIdAsync(first.Id);
            Assert.NotNull(loaded);
            loaded!.Value.Close(T0.AddMinutes(1));
            await repository.SaveAsync(loaded.Value, loaded.Version);
        }

        var second = Position.Create(
            first.ExchangePositionKey,
            MarketCategory.Linear,
            2m,
            T0.AddMinutes(2),
            T0.AddMinutes(2),
            averageEntryPrice: 110m,
            leverage: 2m);
        await using (var dbContext = await CreateMigratedContext())
        {
            await new PositionRepository(dbContext).SaveAsync(second, expectedVersion: null);
        }

        await using var verificationContext = await CreateMigratedContext();
        Assert.Equal(
            2,
            await verificationContext.Positions.CountAsync(position =>
                position.ExchangeAccountId == account.Id.Value &&
                position.InstrumentId == first.ExchangePositionKey.InstrumentId.Value));
    }

    [Fact]
    public async Task Active_exchange_key_is_unique_but_closed_rows_do_not_block_reopening()
    {
        var account = CreateAccount();
        var first = CreatePosition(account.Id);

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(dbContext).SaveAsync(first, expectedVersion: null);
        }

        var duplicate = Position.Create(
            first.ExchangePositionKey,
            MarketCategory.Linear,
            3m,
            T0.AddMinutes(1),
            T0.AddMinutes(1));
        await using (var duplicateContext = await CreateMigratedContext())
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => new PositionRepository(duplicateContext).SaveAsync(duplicate, expectedVersion: null));
            Assert.NotNull(exception);
        }

        await using (var closeContext = await CreateMigratedContext())
        {
            var repository = new PositionRepository(closeContext);
            var loaded = await repository.GetByIdAsync(first.Id);
            Assert.NotNull(loaded);
            loaded!.Value.Close(T0.AddMinutes(2));
            await repository.SaveAsync(loaded.Value, loaded.Version);
        }

        var reopened = Position.Create(
            first.ExchangePositionKey,
            MarketCategory.Linear,
            4m,
            T0.AddMinutes(3),
            T0.AddMinutes(3));
        await using var reopenContext = await CreateMigratedContext();
        await new PositionRepository(reopenContext).SaveAsync(reopened, expectedVersion: null);
    }

    [Fact]
    public async Task PortfolioState_keeps_snapshot_history_and_returns_the_latest_snapshot()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
        await new PositionRepository(dbContext).SaveAsync(position, expectedVersion: null);
        var repository = new PortfolioStateRepository(dbContext);

        var first = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1000m, 800m, T0, 1000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var second = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1100m, 700m, T0.AddMinutes(2), 1100m),
            T0.AddMinutes(3),
            TimeSpan.FromMinutes(5));

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);

        var stateCount = await dbContext.PortfolioStates.CountAsync(state =>
            state.ExchangeAccountId == account.Id.Value);
        Assert.Equal(2, stateCount);

        await using var reloadedContext = await CreateMigratedContext();
        var latest = await new PortfolioStateRepository(reloadedContext).GetLatestAsync(account.Id);

        Assert.NotNull(latest);
        Assert.Equal(second.CalculatedAt, latest!.CalculatedAt);
        Assert.Equal(second.StaleAfter, latest.StaleAfter);
        Assert.Equal(second.Capital, latest.Capital);
        Assert.Equal(second.Positions.ToArray(), latest.Positions.ToArray());
        Assert.Equal(second.GrossExposure, latest.GrossExposure);
        Assert.Equal(second.LongExposure, latest.LongExposure);
        Assert.Equal(second.ShortExposure, latest.ShortExposure);
        Assert.Equal(second.NetExposure, latest.NetExposure);
        Assert.Equal(second.TotalUnrealizedPnl, latest.TotalUnrealizedPnl);
        Assert.Equal(second.UsedCapital, latest.UsedCapital);
        Assert.Equal(second.FreeCapital, latest.FreeCapital);
        Assert.Equal(second.FreeCapitalPercent, latest.FreeCapitalPercent);
        Assert.Equal(second.GrossExposureToEquityPercent, latest.GrossExposureToEquityPercent);
        Assert.Equal(second.LargestPositionConcentrationPercent, latest.LargestPositionConcentrationPercent);
        Assert.Equal(second.LargestPositionId, latest.LargestPositionId);
        Assert.Equal(second.IsComplete, latest.IsComplete);
        Assert.Equal(second.IsFresh, latest.IsFresh);
    }

    [Fact]
    public async Task PositionAssessment_round_trips_input_versions_reasons_and_rule_version()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);
        var inputVersions = new PositionAssessmentInputVersions(
            position.Id,
            account.Id,
            position.ExchangePositionKey.InstrumentId,
            T0.AddMinutes(3),
            T0.AddMinutes(4),
            T0.AddMinutes(5));
        var assessment = PositionAssessment.Create(
            inputVersions,
            new RuleVersion("assessment-v7"),
            RiskIncreasePolicyResult.Blocked(
                [ReasonCode.InsufficientFreeCapital, ReasonCode.GrossExposureLimitExceeded]),
            [],
            T0.AddMinutes(6),
            T0.AddHours(1));

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(dbContext).SaveAsync(position, expectedVersion: null);
            await new PositionAssessmentRepository(dbContext).SaveAsync(assessment);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var reloaded = await new PositionAssessmentRepository(reloadedContext).GetByIdAsync(assessment.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(assessment.Id, reloaded!.Id);
        Assert.Equal(assessment.InputVersions, reloaded.InputVersions);
        Assert.Equal(assessment.RuleVersion, reloaded.RuleVersion);
        Assert.Equal(assessment.CreatedAt, reloaded.CreatedAt);
        Assert.Equal(assessment.ValidUntil, reloaded.ValidUntil);
        Assert.Equal(assessment.PortfolioRiskDecision, reloaded.PortfolioRiskDecision);
        Assert.Equal(assessment.ReasonCodes.ToArray(), reloaded.ReasonCodes.ToArray());
    }

    [Fact]
    public async Task Recommendation_round_trips_after_a_lifecycle_transition()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);
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
        var recommendation = Recommendation.Create(
            assessment,
            PositionAction.Reduce,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v3"),
            [],
            T0.AddMinutes(4),
            T0.AddMinutes(30));
        recommendation.Acknowledge(T0.AddMinutes(5));

        await using (var dbContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(dbContext).SaveAsync(position, expectedVersion: null);
            await new PositionAssessmentRepository(dbContext).SaveAsync(assessment);
            await new RecommendationRepository(dbContext).SaveAsync(recommendation, expectedVersion: null);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var reloaded = await new RecommendationRepository(reloadedContext).GetByIdAsync(recommendation.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(ConcurrencyVersion.Initial, reloaded!.Version);
        var reloadedRecommendation = reloaded.Value;
        Assert.Equal(recommendation.Id, reloadedRecommendation.Id);
        Assert.Equal(recommendation.AssessmentId, reloadedRecommendation.AssessmentId);
        Assert.Equal(recommendation.PositionId, reloadedRecommendation.PositionId);
        Assert.Equal(recommendation.RecommendedAction, reloadedRecommendation.RecommendedAction);
        Assert.Equal(recommendation.AddDecision, reloadedRecommendation.AddDecision);
        Assert.Equal(recommendation.PolicyVersion, reloadedRecommendation.PolicyVersion);
        Assert.Equal(recommendation.ReasonCodes.ToArray(), reloadedRecommendation.ReasonCodes.ToArray());
        Assert.Equal(assessment.ReasonCodes.ToArray(), reloadedRecommendation.ReasonCodes.ToArray());
        Assert.Equal(RecommendationStatus.Acknowledged, reloadedRecommendation.Status);
        Assert.Equal(recommendation.AcknowledgedAt, reloadedRecommendation.AcknowledgedAt);
        Assert.Null(reloadedRecommendation.DismissedAt);
        Assert.Null(reloadedRecommendation.SupersededAt);
        Assert.Null(reloadedRecommendation.ExpiredAt);
    }

    [Fact]
    public async Task Critical_columns_use_relational_postgresql_types()
    {
        await using var dbContext = await CreateMigratedContext();

        Assert.Equal("uuid", await ReadColumnType(dbContext, "positions", "position_id"));
        Assert.Equal("uuid", await ReadColumnType(dbContext, "positions", "exchange_account_id"));
        Assert.Equal("numeric", await ReadColumnType(dbContext, "positions", "position_value"));
        Assert.Equal("numeric", await ReadColumnType(dbContext, "position_changes", "after_size"));
        Assert.Equal("timestamp with time zone", await ReadColumnType(dbContext, "positions", "last_observed_at"));
        Assert.Equal("NO", await ReadColumnNullability(dbContext, "positions", "size"));

        Assert.Equal("bigint", await ReadColumnType(dbContext, "exchange_accounts", "version"));
        Assert.Equal("bigint", await ReadColumnType(dbContext, "positions", "version"));
        Assert.Equal("bigint", await ReadColumnType(dbContext, "recommendations", "version"));
        Assert.Equal("NO", await ReadColumnNullability(dbContext, "exchange_accounts", "version"));
        Assert.Equal("NO", await ReadColumnNullability(dbContext, "positions", "version"));
        Assert.Equal("NO", await ReadColumnNullability(dbContext, "recommendations", "version"));
    }

    [Fact]
    public async Task Position_foreign_key_rejects_an_orphan_position()
    {
        var orphan = CreatePosition(ExchangeAccountId.New());
        await using var dbContext = await CreateMigratedContext();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => new PositionRepository(dbContext).SaveAsync(orphan, expectedVersion: null));
    }

    [Fact]
    public async Task Version_check_constraint_rejects_non_positive_values()
    {
        var account = CreateAccount();
        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE exchange_accounts SET version = 0 WHERE exchange_account_id = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = account.Id.Value;
        command.Parameters.Add(parameter);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());

        Assert.Equal("23514", exception.SqlState);
        Assert.Equal("ck_exchange_accounts_version_positive", exception.ConstraintName);
    }

    [Fact]
    public async Task Saving_a_new_row_with_a_null_expected_version_conflicts_when_the_row_already_exists()
    {
        var account = CreateAccount();
        await using var dbContext = await CreateMigratedContext();
        await new ExchangeAccountRepository(dbContext).SaveAsync(account, expectedVersion: null);

        // Re-inserting with expectedVersion: null (a "blind" insert) must be treated as a
        // conflict, not a silent overwrite, once the row already exists.
        await using var otherContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new ExchangeAccountRepository(otherContext).SaveAsync(account, expectedVersion: null));
    }

    [Fact]
    public async Task Position_blind_insert_conflicts_when_the_row_already_exists()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
        }

        await using var readerContext = await CreateMigratedContext();
        var existing = await new PositionRepository(readerContext).GetByIdAsync(position.Id);
        Assert.NotNull(existing);

        await using var insertContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new PositionRepository(insertContext)
                .SaveAsync(existing!.Value, expectedVersion: null));
    }

    [Fact]
    public async Task Recommendation_blind_insert_conflicts_when_the_row_already_exists()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);
        var assessment = CreateAssessment(account, position);
        var recommendation = Recommendation.Create(
            assessment,
            PositionAction.Reduce,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v4"),
            [],
            T0.AddMinutes(4),
            T0.AddMinutes(30));

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
            await new PositionAssessmentRepository(setupContext).SaveAsync(assessment);
            await new RecommendationRepository(setupContext)
                .SaveAsync(recommendation, expectedVersion: null);
        }

        await using var readerContext = await CreateMigratedContext();
        var existing = await new RecommendationRepository(readerContext)
            .GetByIdAsync(recommendation.Id);
        Assert.NotNull(existing);

        await using var insertContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new RecommendationRepository(insertContext)
                .SaveAsync(existing!.Value, expectedVersion: null));
    }

    [Fact]
    public async Task Concurrent_insert_writers_produce_exactly_one_winner_and_one_conflict()
    {
        var account = CreateAccount();
        await using (var setupContext = await CreateMigratedContext())
        {
            Assert.True(await setupContext.Database.CanConnectAsync());
        }

        var barrier = new ExchangeAccountLookupBarrier();
        await using var writerAContext = CreateContext(barrier);
        await using var writerBContext = CreateContext(barrier);

        var outcomes = await Task.WhenAll(
            CaptureSaveOutcome(() =>
                new ExchangeAccountRepository(writerAContext).SaveAsync(account, expectedVersion: null)),
            CaptureSaveOutcome(() =>
                new ExchangeAccountRepository(writerBContext).SaveAsync(account, expectedVersion: null)));

        Assert.Equal(1, outcomes.Count(outcome => outcome is ConcurrencyVersion));
        Assert.Single(outcomes.OfType<ConcurrencyConflictException>());
        Assert.All(outcomes, outcome => Assert.True(
            outcome is ConcurrencyVersion or ConcurrencyConflictException));

        await using var verificationContext = await CreateMigratedContext();
        var persisted = await verificationContext.ExchangeAccounts
            .SingleAsync(item => item.Id == account.Id.Value);
        Assert.Equal(1L, persisted.Version);
        Assert.Equal(
            1,
            await verificationContext.ExchangeAccounts.CountAsync(item => item.Id == account.Id.Value));
    }

    [Fact]
    public async Task Concurrent_writers_racing_on_the_same_version_produce_exactly_one_winner()
    {
        var account = CreateAccount();
        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
        }

        await using var readerAContext = await CreateMigratedContext();
        var readerA = await new ExchangeAccountRepository(readerAContext).GetByIdAsync(account.Id);
        await using var readerBContext = await CreateMigratedContext();
        var readerB = await new ExchangeAccountRepository(readerBContext).GetByIdAsync(account.Id);

        Assert.NotNull(readerA);
        Assert.NotNull(readerB);
        Assert.Equal(readerA!.Version, readerB!.Version);

        var updatedByA = ExchangeAccount.Create(
            account.Id,
            account.UserId,
            account.ExchangeId,
            ExchangeAccountConnectionStatus.Unavailable,
            account.Capabilities,
            T0,
            "writer A");
        var updatedByB = ExchangeAccount.Create(
            account.Id,
            account.UserId,
            account.ExchangeId,
            ExchangeAccountConnectionStatus.Disabled,
            account.Capabilities,
            T0,
            "writer B");

        await using var writerAContext = await CreateMigratedContext();
        var versionAfterA = await new ExchangeAccountRepository(writerAContext)
            .SaveAsync(updatedByA, readerA.Version);
        Assert.Equal(readerA.Version.Next(), versionAfterA);

        await using var writerBContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new ExchangeAccountRepository(writerBContext).SaveAsync(updatedByB, readerB.Version));

        await using var verificationContext = await CreateMigratedContext();
        var current = await new ExchangeAccountRepository(verificationContext).GetByIdAsync(account.Id);
        Assert.NotNull(current);
        Assert.Equal(versionAfterA, current!.Version);
        Assert.Equal("writer A", current.Value.LastError);
    }

    [Fact]
    public async Task Sequential_saves_increment_the_version_by_one_each_time()
    {
        var account = CreateAccount();
        await using var dbContext = await CreateMigratedContext();
        var repository = new ExchangeAccountRepository(dbContext);
        var version = await repository.SaveAsync(account, expectedVersion: null);
        Assert.Equal(new ConcurrencyVersion(1), version);

        for (var expected = 2L; expected <= 5; expected++)
        {
            var next = ExchangeAccount.Create(
                account.Id,
                account.UserId,
                account.ExchangeId,
                ExchangeAccountConnectionStatus.Connected,
                account.Capabilities,
                T0.AddMinutes(expected),
                $"sequential save #{expected}");
            version = await repository.SaveAsync(next, version);
            Assert.Equal(new ConcurrencyVersion(expected), version);
        }

        var reloaded = await repository.GetByIdAsync(account.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(new ConcurrencyVersion(5), reloaded!.Version);
        Assert.Equal("sequential save #5", reloaded.Value.LastError);
    }

    [Fact]
    public async Task A_stale_writer_conflicts_and_leaves_no_new_position_history()
    {
        // Simulates two concurrent synchronization jobs that both observed the exact same
        // exchange snapshot for the same position and race to persist it: only the first
        // writer may commit; the second (stale) writer must be rejected atomically, without
        // leaving behind a duplicate (or any) history row and without double-bumping the
        // version.
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
        }

        await using var readerAContext = await CreateMigratedContext();
        var readerA = await new PositionRepository(readerAContext).GetByIdAsync(position.Id);
        await using var readerBContext = await CreateMigratedContext();
        var readerB = await new PositionRepository(readerBContext).GetByIdAsync(position.Id);
        Assert.NotNull(readerA);
        Assert.NotNull(readerB);
        Assert.Equal(readerA!.Version, readerB!.Version);

        var positionA = readerA.Value;
        var positionB = readerB.Value;

        var observedAt = T0.AddMinutes(1);
        positionA.ApplyObservation(
            2m,
            observedAt,
            averageEntryPrice: 105m,
            positionValue: 2100m,
            leverage: positionA.Leverage);
        positionB.ApplyObservation(
            3m,
            observedAt,
            averageEntryPrice: 110m,
            positionValue: 3300m,
            leverage: positionB.Leverage);
        Assert.Equal(positionA.Changes.Count, positionB.Changes.Count);

        await using var writerAContext = await CreateMigratedContext();
        var versionAfterA = await new PositionRepository(writerAContext).SaveAsync(positionA, readerA.Version);
        Assert.Equal(readerA.Version.Next(), versionAfterA);

        var historyAfterA = await CountPositionChanges(position.Id);
        Assert.Equal(positionA.Changes.Count, historyAfterA);

        await using var writerBContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new PositionRepository(writerBContext).SaveAsync(positionB, readerB.Version));

        // The stale writer's rejected update must not leave behind its divergent history row:
        // the row count and values must be exactly what writer A committed.
        var historyAfterFailedWriterB = await CountPositionChanges(position.Id);
        Assert.Equal(historyAfterA, historyAfterFailedWriterB);

        await using var verificationContext = await CreateMigratedContext();
        var current = await new PositionRepository(verificationContext).GetByIdAsync(position.Id);
        Assert.NotNull(current);
        Assert.Equal(versionAfterA, current!.Version);
        Assert.Equal(positionA.Size, current.Value.Size);
        Assert.Equal(positionA.AverageEntryPrice, current.Value.AverageEntryPrice);
        Assert.Equal(2, current.Value.Changes.Count);
        Assert.Equal(2m, current.Value.Changes[1].After.Size);
        Assert.Equal(105m, current.Value.Changes[1].After.AverageEntryPrice);
        Assert.Equal(
            Enumerable.Range(1, 2),
            current.Value.Changes.Select((_, index) => index + 1));

        var persistedHistory = await verificationContext.PositionChanges
            .Where(change => change.PositionId == position.Id.Value)
            .OrderBy(change => change.Sequence)
            .ToArrayAsync();
        Assert.Equal(2, persistedHistory.Length);
        Assert.Equal(
            Enumerable.Range(1, 2),
            persistedHistory.Select(change => change.Sequence));
        Assert.Equal(2m, persistedHistory[1].AfterSize);
        Assert.Equal(105m, persistedHistory[1].AfterAverageEntryPrice);
    }

    [Fact]
    public async Task A_stale_writer_paused_after_position_read_conflicts_at_cas_before_history_validation()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
        }

        await using var readerAContext = await CreateMigratedContext();
        var readerA = await new PositionRepository(readerAContext).GetByIdAsync(position.Id);
        await using var readerBContext = await CreateMigratedContext();
        var readerB = await new PositionRepository(readerBContext).GetByIdAsync(position.Id);
        Assert.NotNull(readerA);
        Assert.NotNull(readerB);

        var positionA = readerA!.Value;
        var positionB = readerB!.Value;
        var observedAt = T0.AddMinutes(1);
        positionA.ApplyObservation(
            2m,
            observedAt,
            averageEntryPrice: 105m,
            positionValue: 2100m,
            leverage: positionA.Leverage);
        positionB.ApplyObservation(
            3m,
            observedAt,
            averageEntryPrice: 110m,
            positionValue: 3300m,
            leverage: positionB.Leverage);

        var pause = new PositionLookupPauseInterceptor();
        await using var writerBContext = CreateContext(pause);
        var writerBTask = new PositionRepository(writerBContext)
            .SaveAsync(positionB, readerB.Version);
        await pause.PositionLookupReached.WaitAsync(TimeSpan.FromSeconds(30));

        await using var writerAContext = await CreateMigratedContext();
        var versionAfterA = await new PositionRepository(writerAContext)
            .SaveAsync(positionA, readerA.Version);
        Assert.Equal(new ConcurrencyVersion(2), versionAfterA);

        pause.ReleasePositionLookup();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => writerBTask);

        await using var verificationContext = await CreateMigratedContext();
        var current = await new PositionRepository(verificationContext).GetByIdAsync(position.Id);
        Assert.NotNull(current);
        Assert.Equal(new ConcurrencyVersion(2), current!.Version);
        Assert.Equal(positionA.Size, current.Value.Size);
        Assert.Equal(positionA.AverageEntryPrice, current.Value.AverageEntryPrice);
        Assert.Equal(2, current.Value.Changes.Count);
        Assert.Equal(2m, current.Value.Changes[1].After.Size);
        Assert.Equal(105m, current.Value.Changes[1].After.AverageEntryPrice);

        var persistedHistory = await verificationContext.PositionChanges
            .Where(change => change.PositionId == position.Id.Value)
            .OrderBy(change => change.Sequence)
            .ToArrayAsync();
        Assert.Equal(2, persistedHistory.Length);
        Assert.Equal(
            Enumerable.Range(1, 2),
            persistedHistory.Select(change => change.Sequence));
        Assert.Equal(2m, persistedHistory[1].AfterSize);
        Assert.Equal(105m, persistedHistory[1].AfterAverageEntryPrice);
    }

    [Fact]
    public async Task Invalid_history_after_a_successful_cas_rolls_back_the_position_update()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
        }

        await using var readerContext = await CreateMigratedContext();
        var loaded = await new PositionRepository(readerContext).GetByIdAsync(position.Id);
        Assert.NotNull(loaded);

        var invalidInitialChange = loaded!.Value.Changes[0] with
        {
            Cause = PositionChangeCause.ExchangeObservation,
        };
        var invalidPosition = Position.Restore(
            loaded.Value.Id,
            loaded.Value.ExchangePositionKey,
            loaded.Value.MarketCategory,
            loaded.Value.Size,
            loaded.Value.FirstDetectedAt,
            loaded.Value.LastObservedAt,
            loaded.Value.TrackingState,
            loaded.Value.ClosedAt,
            [invalidInitialChange],
            loaded.Value.AverageEntryPrice,
            loaded.Value.PositionValue,
            loaded.Value.Leverage,
            loaded.Value.MarkPrice,
            loaded.Value.BreakEvenPrice,
            loaded.Value.LiquidationPrice,
            loaded.Value.UnrealizedPnl,
            loaded.Value.TakeProfit,
            loaded.Value.StopLoss,
            loaded.Value.TrailingStop);

        await using var writerContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PositionRepository(writerContext)
                .SaveAsync(invalidPosition, loaded.Version));

        await using var verificationContext = await CreateMigratedContext();
        var current = await new PositionRepository(verificationContext).GetByIdAsync(position.Id);
        Assert.NotNull(current);
        Assert.Equal(ConcurrencyVersion.Initial, current!.Version);
        Assert.Equal(position.Size, current.Value.Size);
        Assert.Single(current.Value.Changes);
        Assert.Equal(PositionChangeCause.InitialObservation, current.Value.Changes[0].Cause);

        var persistedHistory = await verificationContext.PositionChanges
            .Where(change => change.PositionId == position.Id.Value)
            .OrderBy(change => change.Sequence)
            .ToArrayAsync();
        Assert.Single(persistedHistory);
        Assert.Equal(PositionChangeCause.InitialObservation, persistedHistory[0].Cause);
    }

    [Fact]
    public async Task A_stale_dynamic_only_position_update_conflicts_without_changing_history()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
        }

        await using var readerAContext = await CreateMigratedContext();
        var readerA = await new PositionRepository(readerAContext).GetByIdAsync(position.Id);
        await using var readerBContext = await CreateMigratedContext();
        var readerB = await new PositionRepository(readerBContext).GetByIdAsync(position.Id);
        Assert.NotNull(readerA);
        Assert.NotNull(readerB);

        var historyCount = readerA!.Value.Changes.Count;
        var dynamicChangeA = readerA.Value.ApplyObservation(
            readerA.Value.Size,
            T0.AddMinutes(1),
            averageEntryPrice: readerA.Value.AverageEntryPrice,
            positionValue: 2000m,
            leverage: readerA.Value.Leverage,
            markPrice: 222m,
            breakEvenPrice: readerA.Value.BreakEvenPrice,
            liquidationPrice: readerA.Value.LiquidationPrice,
            unrealizedPnl: -7m,
            takeProfit: readerA.Value.TakeProfit,
            stopLoss: readerA.Value.StopLoss,
            trailingStop: readerA.Value.TrailingStop);
        var dynamicChangeB = readerB!.Value.ApplyObservation(
            readerB.Value.Size,
            T0.AddMinutes(1),
            averageEntryPrice: readerB.Value.AverageEntryPrice,
            positionValue: 3000m,
            leverage: readerB.Value.Leverage,
            markPrice: 333m,
            breakEvenPrice: readerB.Value.BreakEvenPrice,
            liquidationPrice: readerB.Value.LiquidationPrice,
            unrealizedPnl: -8m,
            takeProfit: readerB.Value.TakeProfit,
            stopLoss: readerB.Value.StopLoss,
            trailingStop: readerB.Value.TrailingStop);
        Assert.Null(dynamicChangeA);
        Assert.Null(dynamicChangeB);

        await using var writerAContext = await CreateMigratedContext();
        var versionAfterA = await new PositionRepository(writerAContext)
            .SaveAsync(readerA.Value, readerA.Version);
        Assert.Equal(readerA.Version.Next(), versionAfterA);

        await using var writerBContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new PositionRepository(writerBContext).SaveAsync(readerB.Value, readerB.Version));

        await using var verificationContext = await CreateMigratedContext();
        var current = await new PositionRepository(verificationContext).GetByIdAsync(position.Id);
        Assert.NotNull(current);
        Assert.Equal(versionAfterA, current!.Version);
        Assert.Equal(2000m, current.Value.PositionValue);
        Assert.Equal(222m, current.Value.MarkPrice);
        Assert.Equal(-7m, current.Value.UnrealizedPnl);
        Assert.Equal(historyCount, current.Value.Changes.Count);
        Assert.Equal(
            Enumerable.Range(1, historyCount),
            current.Value.Changes.Select((_, index) => index + 1));
    }

    [Fact]
    public async Task A_stale_recommendation_lifecycle_update_conflicts()
    {
        var account = CreateAccount();
        var position = CreatePosition(account.Id);
        var assessment = CreateAssessment(account, position);
        var recommendation = Recommendation.Create(
            assessment,
            PositionAction.Reduce,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v4"),
            [],
            T0.AddMinutes(4),
            T0.AddMinutes(30));

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
            await new PositionRepository(setupContext).SaveAsync(position, expectedVersion: null);
            await new PositionAssessmentRepository(setupContext).SaveAsync(assessment);
            await new RecommendationRepository(setupContext)
                .SaveAsync(recommendation, expectedVersion: null);
        }

        await using var readerAContext = await CreateMigratedContext();
        var readerA = await new RecommendationRepository(readerAContext)
            .GetByIdAsync(recommendation.Id);
        await using var readerBContext = await CreateMigratedContext();
        var readerB = await new RecommendationRepository(readerBContext)
            .GetByIdAsync(recommendation.Id);
        Assert.NotNull(readerA);
        Assert.NotNull(readerB);

        readerA!.Value.Acknowledge(T0.AddMinutes(5));
        readerB!.Value.Dismiss(T0.AddMinutes(5));

        await using var writerAContext = await CreateMigratedContext();
        var versionAfterA = await new RecommendationRepository(writerAContext)
            .SaveAsync(readerA.Value, readerA.Version);
        Assert.Equal(readerA.Version.Next(), versionAfterA);

        await using var writerBContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new RecommendationRepository(writerBContext)
                .SaveAsync(readerB.Value, readerB.Version));

        await using var verificationContext = await CreateMigratedContext();
        var current = await new RecommendationRepository(verificationContext)
            .GetByIdAsync(recommendation.Id);
        Assert.NotNull(current);
        Assert.Equal(versionAfterA, current!.Version);
        Assert.Equal(RecommendationStatus.Acknowledged, current.Value.Status);
        Assert.Equal(T0.AddMinutes(5), current.Value.AcknowledgedAt);
        Assert.Null(current.Value.DismissedAt);
    }

    [Fact]
    public async Task Updating_a_row_that_was_deleted_concurrently_conflicts_instead_of_reinserting()
    {
        var account = CreateAccount();
        Versioned<ExchangeAccount>? readAccount;

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(account, expectedVersion: null);
        }

        await using (var readerContext = await CreateMigratedContext())
        {
            readAccount = await new ExchangeAccountRepository(readerContext).GetByIdAsync(account.Id);
        }

        Assert.NotNull(readAccount);

        await using (var deleteContext = await CreateMigratedContext())
        {
            var entity = await deleteContext.ExchangeAccounts
                .SingleAsync(a => a.Id == account.Id.Value);
            deleteContext.ExchangeAccounts.Remove(entity);
            await deleteContext.SaveChangesAsync();
        }

        var updated = ExchangeAccount.Create(
            account.Id,
            account.UserId,
            account.ExchangeId,
            ExchangeAccountConnectionStatus.Disabled,
            account.Capabilities,
            T0,
            "should not resurrect the row");

        await using var writerContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new ExchangeAccountRepository(writerContext).SaveAsync(updated, readAccount!.Version));

        await using var verificationContext = await CreateMigratedContext();
        Assert.False(await verificationContext.ExchangeAccounts.AnyAsync(a => a.Id == account.Id.Value));
    }

    private async Task<int> CountPositionChanges(PositionId id)
    {
        await using var dbContext = await CreateMigratedContext();
        return await dbContext.PositionChanges.CountAsync(change => change.PositionId == id.Value);
    }

    private TradeSystemDbContext CreateContext(DbCommandInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name))
            .AddInterceptors(interceptor)
            .Options);

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static ExchangeAccount CreateAccount() =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            T0.AddMinutes(-1),
            "last successful read");

    private static PositionAssessment CreateAssessment(
        ExchangeAccount account,
        Position position) =>
        PositionAssessment.Create(
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

    private static Position CreatePosition(ExchangeAccountId accountId) =>
        Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                1),
            MarketCategory.Linear,
            1.123456789012345678m,
            T0,
            T0,
            averageEntryPrice: 100.123456789012345678m,
            positionValue: 1234.567890123456789012m,
            leverage: 2m,
            markPrice: 101.123456789012345678m,
            breakEvenPrice: 99.123456789012345678m,
            liquidationPrice: 50.123456789012345678m,
            unrealizedPnl: -1.123456789012345678m,
            takeProfit: 120.123456789012345678m,
            stopLoss: 90.123456789012345678m,
            trailingStop: 95.123456789012345678m);

    private static DateTimeOffset CanonicalTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static async Task<string> ReadColumnType(
        TradeSystemDbContext dbContext,
        string tableName,
        string columnName)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table_name AND column_name = @column_name
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@table_name";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);
        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@column_name";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<string>(value);
    }

    private static async Task<string> ReadColumnNullability(
        TradeSystemDbContext dbContext,
        string tableName,
        string columnName)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table_name AND column_name = @column_name
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@table_name";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);
        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@column_name";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<string>(value);
    }

    private static async Task<object> CaptureSaveOutcome(
        Func<Task<ConcurrencyVersion>> save)
    {
        try
        {
            return await save();
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class ExchangeAccountLookupBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> lookupsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> lookupsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int startedCount;
        private int completedCount;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsExchangeAccountLookup(command))
            {
                if (Interlocked.Increment(ref startedCount) == 2)
                    lookupsStarted.TrySetResult(true);

                await lookupsStarted.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsExchangeAccountLookup(command))
            {
                if (Interlocked.Increment(ref completedCount) == 2)
                    lookupsCompleted.TrySetResult(true);

                await lookupsCompleted.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        private static bool IsExchangeAccountLookup(DbCommand command) =>
            command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("exchange_accounts", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PositionLookupPauseInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> positionLookupReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> releasePositionLookup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PositionLookupReached => positionLookupReached.Task;

        public void ReleasePositionLookup() => releasePositionLookup.TrySetResult(true);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsPositionLookup(command))
            {
                positionLookupReached.TrySetResult(true);
                await releasePositionLookup.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        private static bool IsPositionLookup(DbCommand command) =>
            command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains("positions", StringComparison.OrdinalIgnoreCase) &&
            !command.CommandText.Contains("position_changes", StringComparison.OrdinalIgnoreCase);
    }
}
