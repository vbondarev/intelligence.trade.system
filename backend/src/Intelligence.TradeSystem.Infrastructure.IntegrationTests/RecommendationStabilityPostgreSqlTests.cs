using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Recommendations;
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
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class RecommendationStabilityPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Current_lookup_and_pending_state_round_trip_are_user_scoped_and_versioned()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var pendingState = new RecommendationStabilityState(
            RecommendationSemanticState.From(aggregate.Recommendation),
            T0.AddMinutes(5),
            T0.AddMinutes(6),
            2);
        var snapshot = new RecommendationStabilityStateSnapshot(
            aggregate.Recommendation.Id,
            pendingState);

        await using (var context = await CreateMigratedContext())
        {
            var stateRepository = new RecommendationStabilityStateRepository(context);
            var savedVersion = await stateRepository.SaveAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                snapshot,
                new RecommendationStabilityStateExpectation.Absent());
            Assert.Equal(ConcurrencyVersion.Initial, savedVersion);
        }

        await using (var context = await CreateMigratedContext())
        {
            var recommendations = new RecommendationRepository(context);
            var states = new RecommendationStabilityStateRepository(context);
            var current = await recommendations.GetCurrentForPositionAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            var loaded = await states.GetAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);

            Assert.NotNull(current);
            Assert.Equal(aggregate.Recommendation.Id, current.Value.Id);
            Assert.Equal(ConcurrencyVersion.Initial, current.Version);
            Assert.NotNull(loaded);
            Assert.Equal(ConcurrencyVersion.Initial, loaded.Version);
            Assert.Equal(snapshot.StateId, loaded.Value.StateId);
            Assert.Equal(snapshot.BaselineRecommendationId, loaded.Value.BaselineRecommendationId);
            Assert.Equal(snapshot.State, loaded.Value.State);
        }

        await using (var context = await CreateMigratedContext())
        {
            var foreign = UserId.New();
            var recommendations = new RecommendationRepository(context);
            var states = new RecommendationStabilityStateRepository(context);

            Assert.Null(await recommendations.GetCurrentForPositionAsync(foreign, aggregate.Position.Id));
            Assert.Null(await states.GetAsync(foreign, aggregate.Position.Id));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => states.SaveAsync(
                    foreign,
                    aggregate.Position.Id,
                    snapshot,
                    new RecommendationStabilityStateExpectation.Absent()));
        }
    }

    [Fact]
    public async Task Pending_state_compare_and_swap_rejects_a_stale_writer()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var state = new RecommendationStabilityStateSnapshot(
            aggregate.Recommendation.Id,
            new RecommendationStabilityState(
                RecommendationSemanticState.From(aggregate.Recommendation),
                T0.AddMinutes(5),
                T0.AddMinutes(5),
                1));

        await using (var context = await CreateMigratedContext())
        {
            await new RecommendationStabilityStateRepository(context)
                .SaveAsync(
                    aggregate.Account.UserId,
                    aggregate.Position.Id,
                    state,
                    new RecommendationStabilityStateExpectation.Absent());
        }

        await using var writer = await CreateMigratedContext();
        var repository = new RecommendationStabilityStateRepository(writer);
        var loaded = await repository.GetAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(loaded);

        var next = new RecommendationStabilityStateSnapshot(
            state.BaselineRecommendationId,
            new RecommendationStabilityState(
                state.State.SemanticState,
                state.State.FirstObservedAt,
                T0.AddMinutes(7),
                2));
        var nextVersion = await repository.SaveAsync(
            aggregate.Account.UserId,
            aggregate.Position.Id,
            next,
            new RecommendationStabilityStateExpectation.Present(
                loaded.Value.StateId,
                loaded.Value.BaselineRecommendationId,
                loaded.Version));
        Assert.Equal(loaded.Version.Next(), nextVersion);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => repository.SaveAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                state,
                new RecommendationStabilityStateExpectation.Present(
                    loaded.Value.StateId,
                    loaded.Value.BaselineRecommendationId,
                    loaded.Version)));
    }

    [Fact]
    public async Task Delayed_delete_cannot_remove_recreated_pending_generation_on_same_baseline()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var first = CreatePendingSnapshot(aggregate, T0.AddMinutes(5), 1);
        var second = CreatePendingSnapshot(aggregate, T0.AddMinutes(6), 2);

        RecommendationStabilityStateSnapshot delayed;
        ConcurrencyVersion delayedVersion;
        await using (var context = await CreateMigratedContext())
        {
            var repository = new RecommendationStabilityStateRepository(context);
            await repository.SaveAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                first,
                new RecommendationStabilityStateExpectation.Absent());
            var loaded = await repository.GetAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            Assert.NotNull(loaded);
            delayed = loaded.Value;
            delayedVersion = loaded.Version;
            await repository.DeleteExpectedAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                new RecommendationStabilityStateExpectation.Present(
                    delayed.StateId,
                    delayed.BaselineRecommendationId,
                    delayedVersion));
            await repository.SaveAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                second,
                new RecommendationStabilityStateExpectation.Absent());
        }

        await using var verification = await CreateMigratedContext();
        var verificationRepository = new RecommendationStabilityStateRepository(verification);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => verificationRepository.DeleteExpectedAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                new RecommendationStabilityStateExpectation.Present(
                    delayed.StateId,
                    delayed.BaselineRecommendationId,
                    delayedVersion)));
        var current = await verificationRepository.GetAsync(
            aggregate.Account.UserId,
            aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.NotEqual(delayed.StateId, current.Value.StateId);
        Assert.Equal(2, current.Value.State.ConsecutiveObservations);
    }

    [Fact]
    public async Task Current_lookup_includes_acknowledged_and_excludes_terminal_history()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);

        await using (var context = await CreateMigratedContext())
        {
            var repository = new RecommendationRepository(context);
            var current = await repository.GetCurrentForPositionAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            Assert.NotNull(current);
            current.Value.Acknowledge(T0.AddMinutes(5));
            await repository.SaveAsync(
                aggregate.Account.UserId,
                current.Value,
                current.Version);
        }

        await using (var context = await CreateMigratedContext())
        {
            var repository = new RecommendationRepository(context);
            var acknowledged = await repository.GetCurrentForPositionAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            Assert.NotNull(acknowledged);
            Assert.Equal(RecommendationStatus.Acknowledged, acknowledged.Value.Status);

            acknowledged.Value.Dismiss(T0.AddMinutes(6));
            await repository.SaveAsync(
                aggregate.Account.UserId,
                acknowledged.Value,
                acknowledged.Version);
        }

        await using var verification = await CreateMigratedContext();
        Assert.Null(await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id));
    }

    [Fact]
    public async Task Partial_current_index_rejects_two_active_recommendations()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var second = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            aggregate.Assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            aggregate.Assessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);

        await using var context = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new RecommendationRepository(context)
                .SaveAsync(aggregate.Account.UserId, second, expectedVersion: null));
    }

    [Fact]
    public async Task Replacement_transaction_updates_old_then_inserts_successor_atomically()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var successor = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            aggregate.Assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            aggregate.Assessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);
        aggregate.Recommendation.SupersedeBy(successor);

        await using (var context = await CreateMigratedContext())
        {
            var transaction = new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context));
            await transaction.ReplaceAsync(
                aggregate.Account.UserId,
                aggregate.Recommendation,
                successor,
                new RecommendationCurrentExpectation.Present(
                    aggregate.Recommendation.Id,
                    ConcurrencyVersion.Initial),
                new RecommendationStabilityStateExpectation.Absent());
        }

        await using var verification = await CreateMigratedContext();
        var repository = new RecommendationRepository(verification);
        var current = await repository.GetCurrentForPositionAsync(
            aggregate.Account.UserId,
            aggregate.Position.Id);
        var old = await repository.GetByIdAsync(
            aggregate.Account.UserId,
            aggregate.Recommendation.Id);

        Assert.NotNull(current);
        Assert.Equal(successor.Id, current.Value.Id);
        Assert.NotNull(old);
        Assert.Equal(RecommendationStatus.Superseded, old.Value.Status);
        Assert.Equal(successor.Id, old.Value.SupersededByRecommendationId);
    }

    [Fact]
    public async Task Publication_conflicts_when_pending_appears_after_absent_read()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var pending = CreatePendingSnapshot(aggregate, T0.AddMinutes(5), 1);
        await using (var pendingContext = await CreateMigratedContext())
        {
            await new RecommendationStabilityStateRepository(pendingContext)
                .SaveAsync(
                    aggregate.Account.UserId,
                    aggregate.Position.Id,
                    pending,
                    new RecommendationStabilityStateExpectation.Absent());
        }

        var successor = CreateSuccessor(aggregate);
        aggregate.Recommendation.SupersedeBy(successor);
        await using var context = await CreateMigratedContext();
        var transaction = new RecommendationPublicationTransaction(
            context,
            new RecommendationRepository(context),
            new RecommendationStabilityStateRepository(context));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => transaction.ReplaceAsync(
                aggregate.Account.UserId,
                aggregate.Recommendation,
                successor,
                new RecommendationCurrentExpectation.Present(
                    aggregate.Recommendation.Id,
                    ConcurrencyVersion.Initial),
                new RecommendationStabilityStateExpectation.Absent()));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        var restoredPending = await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Equal(aggregate.Recommendation.Id, current.Value.Id);
        Assert.NotNull(restoredPending);
    }

    [Fact]
    public async Task Keep_existing_conflicts_when_current_changes_before_confirmation()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var successor = CreateSuccessor(aggregate);
        aggregate.Recommendation.SupersedeBy(successor);
        await using (var context = await CreateMigratedContext())
        {
            await new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context))
                .ReplaceAsync(
                    aggregate.Account.UserId,
                    aggregate.Recommendation,
                    successor,
                    new RecommendationCurrentExpectation.Present(
                        aggregate.Recommendation.Id,
                        ConcurrencyVersion.Initial),
                    new RecommendationStabilityStateExpectation.Absent());
        }

        await using var staleContext = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new RecommendationPublicationTransaction(
                staleContext,
                new RecommendationRepository(staleContext),
                new RecommendationStabilityStateRepository(staleContext))
                .ConfirmKeepExistingAsync(
                    aggregate.Account.UserId,
                    aggregate.Position.Id,
                    new RecommendationCurrentExpectation.Present(
                        aggregate.Recommendation.Id,
                        ConcurrencyVersion.Initial),
                    new RecommendationStabilityStateExpectation.Absent()));
    }

    [Fact]
    public async Task Keep_existing_conflicts_when_pending_appears_after_absent_read()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        await using (var pendingContext = await CreateMigratedContext())
        {
            await new RecommendationStabilityStateRepository(pendingContext)
                .SaveAsync(
                    aggregate.Account.UserId,
                    aggregate.Position.Id,
                    CreatePendingSnapshot(aggregate, T0.AddMinutes(5), 1),
                    new RecommendationStabilityStateExpectation.Absent());
        }

        await using var context = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context))
                .ConfirmKeepExistingAsync(
                    aggregate.Account.UserId,
                    aggregate.Position.Id,
                    new RecommendationCurrentExpectation.Present(
                        aggregate.Recommendation.Id,
                        ConcurrencyVersion.Initial),
                    new RecommendationStabilityStateExpectation.Absent()));
    }

    [Fact]
    public async Task Recommendation_service_rejects_unpersisted_assessment_without_mutation()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAccountAndPositionAsync(aggregate);

        await using var context = await CreateMigratedContext();
        var recommendationsBefore = await context.Recommendations.CountAsync();
        var statesBefore = await context.RecommendationStabilityStates.CountAsync();
        var service = CreateRecommendationService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(
                aggregate.Account.UserId,
                aggregate.Assessment,
                T0.AddMinutes(4)).AsTask());

        Assert.Equal(recommendationsBefore, await context.Recommendations.CountAsync());
        Assert.Equal(statesBefore, await context.RecommendationStabilityStates.CountAsync());
    }

    [Fact]
    public async Task Recommendation_service_rejects_foreign_assessment_without_disclosing_or_mutating()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var foreignUser = UserId.New();

        await using var context = await CreateMigratedContext();
        var recommendationsBefore = await context.Recommendations.CountAsync();
        var statesBefore = await context.RecommendationStabilityStates.CountAsync();
        var service = CreateRecommendationService(context);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(
                foreignUser,
                aggregate.Assessment,
                T0.AddMinutes(4)).AsTask());

        Assert.Equal(recommendationsBefore, await context.Recommendations.CountAsync());
        Assert.Equal(statesBefore, await context.RecommendationStabilityStates.CountAsync());
    }

    [Fact]
    public async Task Service_confirmation_progress_survives_new_contexts_and_publishes_after_threshold()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var candidate = CreateStructuredCandidateAssessment(aggregate);
        await PersistAssessmentAsync(candidate, aggregate.Account.UserId);

        var observations = new[]
        {
            T0.AddMinutes(5),
            T0.AddMinutes(6),
            T0.AddMinutes(8)
        };
        RecommendationApplicationResult? published = null;
        foreach (var asOf in observations)
        {
            await using var context = await CreateMigratedContext();
            var result = await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidate, asOf);

            if (asOf == observations[^1])
            {
                published = result;
                Assert.Equal(RecommendationApplicationResultKind.Published, result.Kind);
            }
            else
            {
                Assert.Equal(RecommendationApplicationResultKind.PendingConfirmation, result.Kind);
                await using var stateContext = await CreateMigratedContext();
                var state = await new RecommendationStabilityStateRepository(stateContext)
                    .GetAsync(aggregate.Account.UserId, aggregate.Position.Id);
                Assert.NotNull(state);
                Assert.Equal(asOf == observations[0] ? 1 : 2, state.Value.State.ConsecutiveObservations);
            }
        }

        Assert.NotNull(published);
        await using var verification = await CreateMigratedContext();
        var recommendations = new RecommendationRepository(verification);
        var current = await recommendations.GetCurrentForPositionAsync(
            aggregate.Account.UserId,
            aggregate.Position.Id);
        var old = await recommendations.GetByIdAsync(
            aggregate.Account.UserId,
            aggregate.Recommendation.Id);
        var pending = await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id);

        Assert.NotNull(current);
        Assert.Equal(published.Recommendation!.Id, current.Value.Id);
        Assert.NotNull(old);
        Assert.Equal(RecommendationStatus.Superseded, old.Value.Status);
        Assert.Null(pending);
    }

    [Fact]
    public async Task Service_replay_after_new_context_is_idempotent_for_persisted_pending_state()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var candidate = CreateStructuredCandidateAssessment(aggregate);
        await PersistAssessmentAsync(candidate, aggregate.Account.UserId);
        var asOf = T0.AddMinutes(5);

        await using (var firstContext = await CreateMigratedContext())
        {
            var result = await CreateRecommendationService(firstContext)
                .CreateAsync(aggregate.Account.UserId, candidate, asOf);
            Assert.Equal(RecommendationApplicationResultKind.PendingConfirmation, result.Kind);
        }

        Versioned<RecommendationStabilityStateSnapshot> first;
        await using (var readContext = await CreateMigratedContext())
        {
            first = (await new RecommendationStabilityStateRepository(readContext)
                .GetAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        }

        await using (var replayContext = await CreateMigratedContext())
        {
            var result = await CreateRecommendationService(replayContext)
                .CreateAsync(aggregate.Account.UserId, candidate, asOf);
            Assert.Equal(RecommendationApplicationResultKind.PendingConfirmation, result.Kind);
        }

        await using var verification = await CreateMigratedContext();
        var second = (await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        Assert.Equal(first.Value.StateId, second.Value.StateId);
        Assert.Equal(first.Value.State.FirstObservedAt, second.Value.State.FirstObservedAt);
        Assert.Equal(first.Value.State.LastObservedAt, second.Value.State.LastObservedAt);
        Assert.Equal(first.Value.State.ConsecutiveObservations, second.Value.State.ConsecutiveObservations);
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task Service_candidate_change_resets_observations_without_inheriting_old_semantic_state()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var candidateB = CreateStructuredCandidateAssessment(aggregate);
        var candidateC = CreateStructuredCandidateAssessment(aggregate, totalEquity: 20_000m);
        await PersistAssessmentAsync(candidateB, aggregate.Account.UserId);
        await PersistAssessmentAsync(candidateC, aggregate.Account.UserId);

        await using (var context = await CreateMigratedContext())
            await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidateB, T0.AddMinutes(5));
        await using (var context = await CreateMigratedContext())
            await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidateB, T0.AddMinutes(6));

        Versioned<RecommendationStabilityStateSnapshot> beforeChange;
        await using (var context = await CreateMigratedContext())
        {
            beforeChange = (await new RecommendationStabilityStateRepository(context)
                .GetAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        }
        var candidateCEvaluation = new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy()
            .Evaluate(candidateC, PolicyDefinition.Default, T0.AddMinutes(7));
        Assert.NotEqual(
            beforeChange.Value.State.SemanticState,
            RecommendationSemanticState.From(candidateCEvaluation));

        await using (var context = await CreateMigratedContext())
        {
            var result = await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidateC, T0.AddMinutes(7));
            Assert.Equal(RecommendationApplicationResultKind.PendingConfirmation, result.Kind);
        }

        await using var verification = await CreateMigratedContext();
        var afterChange = (await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        Assert.Equal(beforeChange.Value.StateId, afterChange.Value.StateId);
        Assert.Equal(1, afterChange.Value.State.ConsecutiveObservations);
        Assert.Equal(T0.AddMinutes(7), afterChange.Value.State.FirstObservedAt);
        Assert.NotEqual(
            beforeChange.Value.State.SemanticState,
            afterChange.Value.State.SemanticState);
    }

    [Fact]
    public async Task Service_baseline_replacement_starts_new_pending_generation()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var candidateB = CreateStructuredCandidateAssessment(aggregate);
        var candidateC = CreateStructuredCandidateAssessment(aggregate, totalEquity: 20_000m);
        await PersistAssessmentAsync(candidateB, aggregate.Account.UserId);
        await PersistAssessmentAsync(candidateC, aggregate.Account.UserId);

        foreach (var asOf in new[] { T0.AddMinutes(5), T0.AddMinutes(6), T0.AddMinutes(8) })
        {
            await using var context = await CreateMigratedContext();
            await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidateB, asOf);
        }

        await using (var context = await CreateMigratedContext())
        {
            var currentAfterPublication = await new RecommendationRepository(context)
                .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
            Assert.NotNull(currentAfterPublication);
            var candidateCEvaluation = new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy()
                .Evaluate(candidateC, PolicyDefinition.Default, T0.AddMinutes(9));
            Assert.NotEqual(
                RecommendationSemanticState.From(currentAfterPublication.Value),
                RecommendationSemanticState.From(candidateCEvaluation));
            Assert.Null(await new RecommendationStabilityStateRepository(context)
                .GetAsync(aggregate.Account.UserId, aggregate.Position.Id));
        }

        await using (var context = await CreateMigratedContext())
        {
            var result = await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidateC, T0.AddMinutes(9));
            Assert.Equal(RecommendationApplicationResultKind.PendingConfirmation, result.Kind);
        }

        await using var verification = await CreateMigratedContext();
        var current = (await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        var pending = (await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        Assert.Equal(current.Value.Id, pending.Value.BaselineRecommendationId);
        Assert.NotEqual(aggregate.Recommendation.Id, pending.Value.BaselineRecommendationId);
        Assert.Equal(1, pending.Value.State.ConsecutiveObservations);
        Assert.NotEqual(Guid.Empty, pending.Value.StateId);
    }

    [Fact]
    public async Task Service_uses_persisted_assessment_instead_of_tampered_caller_object()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var persisted = CreateStructuredCandidateAssessment(aggregate);
        await PersistAssessmentAsync(persisted, aggregate.Account.UserId);
        var tamperedInput = new PositionAssessmentInputVersions(
            persisted.PositionId,
            persisted.InputVersions.ExchangeAccountId,
            persisted.InputVersions.InstrumentId,
            persisted.InputVersions.PositionObservedAt,
            persisted.InputVersions.PortfolioCalculatedAt,
            persisted.InputVersions.MarketCapturedAt,
            new PolicyConfigurationIdentity("tampered-policy", "tampered-hash"),
            new PolicyConfigurationIdentity("tampered-policy", "tampered-hash"));
        var tampered = PositionAssessment.Restore(
            persisted.Id,
            tamperedInput,
            persisted.RuleVersion,
            persisted.CreatedAt,
            persisted.ValidUntil,
            persisted.PortfolioRiskDecision,
            persisted.Result,
            persisted.ReasonCodes);

        await using var context = await CreateMigratedContext();
        var result = await CreateRecommendationService(context)
            .CreateAsync(aggregate.Account.UserId, tampered, T0.AddMinutes(5));

        Assert.Equal(RecommendationApplicationResultKind.PendingConfirmation, result.Kind);
        var state = (await new RecommendationStabilityStateRepository(context)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id))!;
        var expectedEvaluation = new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy()
            .Evaluate(persisted, PolicyDefinition.Default, T0.AddMinutes(5));
        Assert.Equal(
            RecommendationSemanticState.From(expectedEvaluation),
            state.Value.State.SemanticState);
    }

    [Fact]
    public async Task Concurrent_services_do_not_publish_duplicate_or_stale_successors()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var candidate = CreateStructuredCandidateAssessment(aggregate);
        await PersistAssessmentAsync(candidate, aggregate.Account.UserId);

        foreach (var asOf in new[] { T0.AddMinutes(5), T0.AddMinutes(6) })
        {
            await using var context = await CreateMigratedContext();
            await CreateRecommendationService(context)
                .CreateAsync(aggregate.Account.UserId, candidate, asOf);
        }

        await using var firstContext = await CreateMigratedContext();
        await using var secondContext = await CreateMigratedContext();
        var first = RunServiceAsync(firstContext, aggregate.Account.UserId, candidate);
        var second = RunServiceAsync(secondContext, aggregate.Account.UserId, candidate);
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome.Result?.Kind == RecommendationApplicationResultKind.Published);
        Assert.All(
            outcomes,
            outcome => Assert.True(
                outcome.Error is null ||
                outcome.Error is ConcurrencyConflictException,
                outcome.Error?.ToString()));

        await using var verification = await CreateMigratedContext();
        var recommendations = verification.Recommendations
            .Where(row => row.PositionId == aggregate.Position.Id.Value)
            .ToArray();
        Assert.Single(recommendations, row =>
            row.Status is RecommendationStatus.Active or RecommendationStatus.Acknowledged);
        Assert.Null(await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id));

        static async Task<(RecommendationApplicationResult? Result, Exception? Error)> RunServiceAsync(
            TradeSystemDbContext context,
            UserId userId,
            PositionAssessment assessment)
        {
            try
            {
                return (
                    await CreateRecommendationService(context)
                        .CreateAsync(userId, assessment, T0.AddMinutes(8)),
                    null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }
    }

    [Fact]
    public async Task Replacement_failure_rolls_back_old_lifecycle_update()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var pending = CreatePendingSnapshot(aggregate, T0.AddMinutes(5), 1);
        await using (var pendingContext = await CreateMigratedContext())
        {
            await new RecommendationStabilityStateRepository(pendingContext)
                .SaveAsync(
                    aggregate.Account.UserId,
                    aggregate.Position.Id,
                    pending,
                    new RecommendationStabilityStateExpectation.Absent());
        }
        var unpersistedAssessment = PositionAssessment.Create(
            aggregate.Assessment.InputVersions,
            new RuleVersion("assessment-v2"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            T0.AddMinutes(5),
            T0.AddHours(1));
        var successor = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            unpersistedAssessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            unpersistedAssessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);
        aggregate.Recommendation.SupersedeBy(successor);

        await using var context = await CreateMigratedContext();
        var transaction = new RecommendationPublicationTransaction(
            context,
            new RecommendationRepository(context),
            new RecommendationStabilityStateRepository(context));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => transaction.ReplaceAsync(
                aggregate.Account.UserId,
                aggregate.Recommendation,
                successor,
                new RecommendationCurrentExpectation.Present(
                    aggregate.Recommendation.Id,
                    ConcurrencyVersion.Initial),
                new RecommendationStabilityStateExpectation.Present(
                    pending.StateId,
                    pending.BaselineRecommendationId,
                    ConcurrencyVersion.Initial)));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Equal(aggregate.Recommendation.Id, current.Value.Id);
        Assert.Equal(RecommendationStatus.Active, current.Value.Status);
        var restoredPending = await new RecommendationStabilityStateRepository(verification)
            .GetAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(restoredPending);
        Assert.Equal(pending.StateId, restoredPending.Value.StateId);
    }

    [Fact]
    public async Task Concurrent_replacements_leave_exactly_one_current_successor()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var successorA = CreateSuccessor(aggregate);
        var successorB = CreateSuccessor(aggregate);

        await using var firstContext = await CreateMigratedContext();
        await using var secondContext = await CreateMigratedContext();
        var firstCurrent = await new RecommendationRepository(firstContext)
            .GetByIdAsync(aggregate.Account.UserId, aggregate.Recommendation.Id);
        var secondCurrent = await new RecommendationRepository(secondContext)
            .GetByIdAsync(aggregate.Account.UserId, aggregate.Recommendation.Id);
        Assert.NotNull(firstCurrent);
        Assert.NotNull(secondCurrent);
        firstCurrent.Value.SupersedeBy(successorA);
        secondCurrent.Value.SupersedeBy(successorB);

        var firstAttempt = ReplaceAsync(
            firstContext,
            aggregate.Account.UserId,
            firstCurrent.Value,
            successorA);
        var secondAttempt = ReplaceAsync(
            secondContext,
            aggregate.Account.UserId,
            secondCurrent.Value,
            successorB);
        var failures = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.Equal(1, failures.Count(failure => failure is ConcurrencyConflictException));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Contains(current.Value.Id, new[] { successorA.Id, successorB.Id });
        Assert.Equal(
            2,
            await verification.Recommendations
                .CountAsync(recommendation => recommendation.PositionId == aggregate.Position.Id.Value));
    }

    [Fact]
    public async Task Concurrent_initial_publication_leaves_one_current_recommendation()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistWithoutRecommendationAsync(aggregate);
        var evaluation = new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy().Evaluate(
            aggregate.Assessment,
            PolicyDefinition.Default,
            T0.AddMinutes(4));
        var candidateA = Recommendation.Create(aggregate.Assessment, evaluation);
        var candidateB = Recommendation.Create(aggregate.Assessment, evaluation);

        await using var firstContext = await CreateMigratedContext();
        await using var secondContext = await CreateMigratedContext();
        var firstAttempt = PublishAsync(
            firstContext,
            aggregate.Account.UserId,
            candidateA);
        var secondAttempt = PublishAsync(
            secondContext,
            aggregate.Account.UserId,
            candidateB);
        var failures = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.Equal(1, failures.Count(failure => failure is ConcurrencyConflictException));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Contains(current.Value.Id, new[] { candidateA.Id, candidateB.Id });
        Assert.Equal(
            1,
            await verification.Recommendations
                .CountAsync(recommendation => recommendation.PositionId == aggregate.Position.Id.Value));
    }

    private async Task PersistAsync(Aggregate aggregate)
    {
        await PersistWithoutRecommendationAsync(aggregate);
        await using var context = await CreateMigratedContext();
        await new RecommendationRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Recommendation, expectedVersion: null);
    }

    private async Task PersistAccountAndPositionAsync(Aggregate aggregate)
    {
        await using var context = await CreateMigratedContext();
        await new ExchangeAccountRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Account, expectedVersion: null);
        await new PositionRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Position, expectedVersion: null);
    }

    private async Task PersistAssessmentAsync(
        PositionAssessment assessment,
        UserId userId)
    {
        await using var context = await CreateMigratedContext();
        await new PositionAssessmentRepository(context).SaveAsync(userId, assessment);
    }

    private async Task PersistWithoutRecommendationAsync(Aggregate aggregate)
    {
        await using var context = await CreateMigratedContext();
        await new ExchangeAccountRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Account, expectedVersion: null);
        await new PositionRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Position, expectedVersion: null);
        await new PositionAssessmentRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Assessment);
    }

    private static async Task<Exception?> ReplaceAsync(
        TradeSystemDbContext context,
        UserId userId,
        Recommendation current,
        Recommendation successor)
    {
        try
        {
            await new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context))
                .ReplaceAsync(
                    userId,
                    current,
                    successor,
                    new RecommendationCurrentExpectation.Present(
                        current.Id,
                        ConcurrencyVersion.Initial),
                    new RecommendationStabilityStateExpectation.Absent());
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> PublishAsync(
        TradeSystemDbContext context,
        UserId userId,
        Recommendation candidate)
    {
        try
        {
            await new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context))
                .PublishInitialAsync(
                    userId,
                    candidate,
                    new RecommendationCurrentExpectation.Absent(),
                    new RecommendationStabilityStateExpectation.Absent());
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Recommendation CreateSuccessor(Aggregate aggregate) =>
        Recommendation.RestoreLegacy(
            RecommendationId.New(),
            aggregate.Assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            aggregate.Assessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);

    private static RecommendationStabilityStateSnapshot CreatePendingSnapshot(
        Aggregate aggregate,
        DateTimeOffset observedAt,
        int observations) =>
        new(
            aggregate.Recommendation.Id,
            new RecommendationStabilityState(
                RecommendationSemanticState.From(aggregate.Recommendation),
                observedAt,
                observedAt,
                observations));

    private static PositionAssessment CreateStructuredCandidateAssessment(
        Aggregate aggregate,
        decimal totalEquity = 10_000m,
        decimal currentPrice = 105m)
    {
        var policy = PolicyDefinition.Default;
        var inputVersions = new PositionAssessmentInputVersions(
            aggregate.Position.Id,
            aggregate.Account.Id,
            aggregate.Position.ExchangePositionKey.InstrumentId,
            T0,
            T0.AddMinutes(1),
            T0.AddMinutes(2),
            policy.Identity,
            policy.Identity);
        var result = new PositionAssessmentResult(
            PositionSide.Long,
            currentPrice,
            new(AssessmentTrendDirection.Bullish, PositionTrendAlignment.Aligned, 0.8m, "4h"),
            new(50m, true, AssessmentMomentumState.Normal, false),
            new(1m, 1m, true, false),
            new(105m, 99m, 1m, 0.7m, 110m, 1m, 0.7m),
            new(1m, 1m, 100m, 105m, AssessmentPricePosition.Above) { IsFavorable = true },
            new(102m, 2.86m, 2m, AssessmentStopState.Protective, AssessmentPricePosition.Above, null, false),
            new(100m, 4.7m, 0m, AssessmentPricePosition.Above) { IsProfitable = true },
            new(40m, 61m, AssessmentLiquidationState.Far),
            new(
                RiskIncreaseDecision.Allowed,
                80m,
                50m,
                10m,
                1m,
                2_000m,
                true,
                true,
                totalEquity,
                8_000m,
                1_000m,
                10m,
                10m,
                100m,
                25m),
            new(AssessmentDataQuality.FreshCompleteReliable, AssessmentDataQuality.FreshCompleteReliable));

        return PositionAssessment.Create(
            inputVersions,
            new RuleVersion("candidate-v2"),
            RiskIncreasePolicyResult.Allowed(),
            result,
            [
                ReasonCode.TrendAligned,
                ReasonCode.MomentumNormal,
                ReasonCode.PnlPositive,
                ReasonCode.StopProtective,
                ReasonCode.LiquidationFar
            ],
            T0.AddMinutes(3),
            T0.AddHours(1));
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static RecommendationService CreateRecommendationService(
        TradeSystemDbContext context) =>
        new(
            new FixedPolicyProvider(),
            new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy(),
            new RecommendationStabilityPolicy(),
            new RecommendationRepository(context),
            new RecommendationStabilityStateRepository(context),
            new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context)),
            new PositionAssessmentRepository(context));

    private sealed class FixedPolicyProvider : IRecommendationPolicyDefinitionProvider
    {
        public ValueTask<PolicyDefinition> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PolicyDefinition.Default);
        }
    }

    private static Aggregate CreateAggregate(UserId userId)
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            T0,
            null);
        var position = Position.Create(
            ExchangePositionKey.Create(
                account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            100m,
            100m,
            2m,
            100m,
            0m);
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
        var recommendation = Recommendation.RestoreLegacy(
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

        return new(account, position, assessment, recommendation);
    }

    private sealed record Aggregate(
        ExchangeAccount Account,
        Position Position,
        PositionAssessment Assessment,
        Recommendation Recommendation);
}
