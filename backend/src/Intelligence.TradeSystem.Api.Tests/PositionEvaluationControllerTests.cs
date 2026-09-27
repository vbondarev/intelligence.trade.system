using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionEvaluationControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly ApiWebApplicationFactory factory;

    public PositionEvaluationControllerTests(ApiWebApplicationFactory factory) =>
        this.factory = factory;

    [Fact]
    public async Task Get_returns_204_when_owned_position_has_no_assessment()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var assessmentRepository = new Mock<IPositionAssessmentRepository>(MockBehavior.Strict);
        assessmentRepository
            .Setup(repository => repository.GetLatestForPositionAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionAssessment?)null);
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(positionRepository.Object, assessmentRepository.Object));

        using var response = await client.GetAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        assessmentRepository.VerifyAll();
    }

    [Fact]
    public async Task Get_rejects_empty_and_malformed_position_ids_with_validation_problems()
    {
        var userId = UserId.New();
        using var client = CreateClient(
            userId,
            CreateService(new Mock<IPositionRepository>(MockBehavior.Strict).Object));

        using var emptyResponse = await client.GetAsync(
            $"/api/v1/positions/{Guid.Empty}/evaluation");
        using var malformedResponse = await client.GetAsync(
            "/api/v1/positions/not-a-guid/evaluation");

        await AssertValidationProblem(emptyResponse);
        await AssertValidationProblem(malformedResponse);
    }

    [Fact]
    public async Task Get_returns_legacy_assessment_without_fabricating_structured_result()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var assessment = CreateLegacyAssessment(position);
        var assessmentRepository = new Mock<IPositionAssessmentRepository>(MockBehavior.Strict);
        assessmentRepository
            .Setup(repository => repository.GetLatestForPositionAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);
        var recommendations = new Mock<IRecommendationRepository>(MockBehavior.Strict);
        recommendations
            .Setup(repository => repository.GetCurrentForPositionAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<Recommendation>?)null);
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                assessmentRepository.Object,
                recommendations.Object));

        using var response = await client.GetAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PositionEvaluationResponse>(
            V1JsonSerializerOptions.Default);
        body!.Assessment.IsLegacy.Should().BeTrue();
        body.Assessment.Result.Should().BeNull();
        body.Recommendation.Should().BeNull();
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("\"recommendation\":null");
        raw.Should().NotContainAny("userId", "credentials", "indicatorDiagnostics");
    }

    [Fact]
    public async Task Post_returns_200_with_a_complete_evaluation_read_model()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var account = ExchangeAccount.Create(
            position.ExchangePositionKey.ExchangeAccountId,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        accountRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        var policyProvider = new Mock<IRecommendationPolicyDefinitionProvider>(MockBehavior.Strict);
        policyProvider
            .Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDefinition.Default);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        market
            .Setup(service => service.BuildSnapshotAsync(
                account.ExchangeId,
                position.ExchangePositionKey.InstrumentId.Value!,
                position.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiSnapshotTestData.CreateSnapshot());
        var assessmentRepository = new Mock<IPositionAssessmentRepository>(MockBehavior.Strict);
        PositionAssessment? persistedAssessment = null;
        assessmentRepository
            .Setup(repository => repository.SaveAsync(
                userId,
                It.IsAny<PositionAssessment>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, PositionAssessment, CancellationToken>(
                (_, assessment, _) => persistedAssessment = assessment)
            .Returns(Task.CompletedTask);
        assessmentRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                It.IsAny<PositionAssessmentId>(),
                It.IsAny<CancellationToken>()))
            .Returns((UserId _, PositionAssessmentId id, CancellationToken _) =>
                Task.FromResult<PositionAssessment?>(
                    persistedAssessment?.Id == id ? persistedAssessment : null));
        var recommendationRepository = new Mock<IRecommendationRepository>(MockBehavior.Strict);
        recommendationRepository
            .Setup(repository => repository.GetCurrentForPositionAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<Recommendation>?)null);
        var stabilityStateRepository =
            new Mock<IRecommendationStabilityStateRepository>(MockBehavior.Strict);
        stabilityStateRepository
            .Setup(repository => repository.GetAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<RecommendationStabilityStateSnapshot>?)null);
        var publicationTransaction =
            new Mock<IRecommendationPublicationTransaction>(MockBehavior.Strict);
        publicationTransaction
            .Setup(transaction => transaction.PublishInitialAsync(
                userId,
                It.IsAny<Recommendation>(),
                It.IsAny<RecommendationCurrentExpectation>(),
                It.IsAny<RecommendationStabilityStateExpectation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var recommendationService = CreateRecommendationService(
            policyProvider.Object,
            recommendationRepository.Object,
            stabilityStateRepository.Object,
            publicationTransaction.Object,
            assessmentRepository.Object);
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                assessmentRepository.Object,
                recommendationRepository.Object,
                market.Object,
                accountRepository.Object,
                portfolioRepository.Object,
                policyProvider.Object,
                recommendationService));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PositionEvaluationResponse>(
            V1JsonSerializerOptions.Default);
        body.Should().NotBeNull();
        body!.PositionId.Should().Be(position.Id.Value);
        body.Assessment.Id.Should().NotBe(Guid.Empty);
        body.Assessment.EvaluatedAt.Should().Be(T0.AddMinutes(2));
        body.Assessment.ValidUntil.Should().BeAfter(body.Assessment.EvaluatedAt);
        body.Assessment.InputIdentity.PositionId.Should().Be(position.Id.Value);
        body.Assessment.InputIdentity.ExchangeAccountId.Should().Be(account.Id.Value);
        body.Assessment.BasePolicyIdentity.Version.Should().NotBeNullOrWhiteSpace();
        body.Assessment.EffectiveConfigurationIdentity.Version.Should().NotBeNullOrWhiteSpace();
        body.Recommendation.Should().NotBeNull();
        body.Recommendation!.AssessmentId.Should().Be(body.Assessment.Id);
        body.Recommendation.Action.Value.Should().NotBe(PositionActionV1.Close);
    }

    [Fact]
    public async Task Post_rejects_empty_and_malformed_position_ids_with_validation_problems()
    {
        var userId = UserId.New();
        using var client = CreateClient(
            userId,
            CreateService(new Mock<IPositionRepository>(MockBehavior.Strict).Object));

        using var emptyResponse = await client.PostAsync(
            $"/api/v1/positions/{Guid.Empty}/evaluation",
            content: null);
        using var malformedResponse = await client.PostAsync(
            "/api/v1/positions/not-a-guid/evaluation",
            content: null);

        await AssertValidationProblem(emptyResponse);
        await AssertValidationProblem(malformedResponse);
    }

    [Fact]
    public async Task Post_returns_404_for_a_missing_position()
    {
        var userId = UserId.New();
        var positionRepository = new Mock<IPositionRepository>(MockBehavior.Strict);
        positionRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                It.IsAny<PositionId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<Position>?)null);
        using var client = CreateClient(
            userId,
            CreateService(positionRepository.Object));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("resource_not_found");
    }

    [Fact]
    public async Task Post_returns_409_when_portfolio_state_is_missing_without_market_io()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var account = ExchangeAccount.Create(
            position.ExchangePositionKey.ExchangeAccountId,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected);
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        accountRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortfolioState?)null);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                exchangeAccountRepository: accountRepository.Object,
                portfolioStateRepository: portfolioRepository.Object,
                marketSnapshotService: market.Object));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("position_not_evaluable");
        problem.Extensions["reason"]!.ToString().Should().Be("portfolioUnavailable");
        market.Verify(
            service => service.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(
        PositionEvaluationNotEvaluableReason.ClosedPosition,
        PositionNotEvaluableReasonV1.ClosedPosition)]
    [InlineData(
        PositionEvaluationNotEvaluableReason.PortfolioUnavailable,
        PositionNotEvaluableReasonV1.PortfolioUnavailable)]
    [InlineData(
        PositionEvaluationNotEvaluableReason.PortfolioInconsistent,
        PositionNotEvaluableReasonV1.PortfolioInconsistent)]
    [InlineData(
        PositionEvaluationNotEvaluableReason.TemporalInconsistency,
        PositionNotEvaluableReasonV1.TemporalInconsistency)]
    public void Not_evaluable_reasons_map_to_typed_v1_values(
        PositionEvaluationNotEvaluableReason reason,
        PositionNotEvaluableReasonV1 v1Reason)
    {
        PositionEvaluationNotEvaluableReasonV1Mapper.ToWireValue(reason).Should().Be(v1Reason);
    }

    [Fact]
    public void Unknown_not_evaluable_reason_is_a_programming_failure()
    {
        var act = () => PositionEvaluationNotEvaluableReasonV1Mapper.ToWireValue(
            (PositionEvaluationNotEvaluableReason)int.MaxValue);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Post_returns_409_with_portfolio_inconsistent_reason()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var account = ExchangeAccount.Create(
            position.ExchangePositionKey.ExchangeAccountId,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected);
        var portfolio = PortfolioState.Create(
            account.Id,
            [],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        accountRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                marketSnapshotService: market.Object,
                exchangeAccountRepository: accountRepository.Object,
                portfolioStateRepository: portfolioRepository.Object));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("position_not_evaluable");
        problem.Extensions["reason"]!.ToString().Should().Be("portfolioInconsistent");
        market.Verify(
            service => service.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Post_maps_evaluation_concurrency_conflict_to_409_problem()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var account = ExchangeAccount.Create(
            position.ExchangePositionKey.ExchangeAccountId,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        accountRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        var policyProvider = new Mock<IRecommendationPolicyDefinitionProvider>(MockBehavior.Strict);
        policyProvider
            .Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDefinition.Default);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        market
            .Setup(service => service.BuildSnapshotAsync(
                account.ExchangeId,
                position.ExchangePositionKey.InstrumentId.Value!,
                position.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiSnapshotTestData.CreateSnapshot());
        var assessmentRepository = new Mock<IPositionAssessmentRepository>(MockBehavior.Strict);
        PositionAssessment? persistedAssessment = null;
        assessmentRepository
            .Setup(repository => repository.SaveAsync(
                userId,
                It.IsAny<PositionAssessment>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, PositionAssessment, CancellationToken>(
                (_, assessment, _) => persistedAssessment = assessment)
            .Returns(Task.CompletedTask);
        assessmentRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                It.IsAny<PositionAssessmentId>(),
                It.IsAny<CancellationToken>()))
            .Returns((UserId _, PositionAssessmentId id, CancellationToken _) =>
                Task.FromResult<PositionAssessment?>(
                    persistedAssessment?.Id == id ? persistedAssessment : null));
        var recommendationRepository = new Mock<IRecommendationRepository>(MockBehavior.Strict);
        recommendationRepository
            .Setup(repository => repository.GetCurrentForPositionAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<Recommendation>?)null);
        var stabilityStateRepository =
            new Mock<IRecommendationStabilityStateRepository>(MockBehavior.Strict);
        stabilityStateRepository
            .Setup(repository => repository.GetAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<RecommendationStabilityStateSnapshot>?)null);
        var publicationTransaction =
            new Mock<IRecommendationPublicationTransaction>(MockBehavior.Strict);
        publicationTransaction
            .Setup(transaction => transaction.PublishInitialAsync(
                userId,
                It.IsAny<Recommendation>(),
                It.IsAny<RecommendationCurrentExpectation>(),
                It.IsAny<RecommendationStabilityStateExpectation>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("test concurrency conflict"));
        var recommendationService = CreateRecommendationService(
            policyProvider.Object,
            recommendationRepository.Object,
            stabilityStateRepository.Object,
            publicationTransaction.Object,
            assessmentRepository.Object);
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                assessmentRepository.Object,
                recommendationRepository.Object,
                market.Object,
                accountRepository.Object,
                portfolioRepository.Object,
                policyProvider.Object,
                recommendationService));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("concurrency_conflict");
        publicationTransaction.Verify(
            transaction => transaction.PublishInitialAsync(
                userId,
                It.IsAny<Recommendation>(),
                It.IsAny<RecommendationCurrentExpectation>(),
                It.IsAny<RecommendationStabilityStateExpectation>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Post_returns_409_for_closed_position_without_loading_market_data()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        position.Close(T0.AddMinutes(1));
        var positionRepository = CreatePositionRepository(userId, position);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        using var client = CreateClient(
            userId,
            CreateService(positionRepository.Object, marketSnapshotService: market.Object));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("position_not_evaluable");
        problem.Extensions["reason"]!.ToString().Should().Be("closedPosition");
        market.Verify(
            service => service.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Post_returns_409_with_temporal_inconsistency_reason()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var account = ExchangeAccount.Create(
            position.ExchangePositionKey.ExchangeAccountId,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(3),
            TimeSpan.FromMinutes(5));
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        accountRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        var policyProvider = new Mock<IRecommendationPolicyDefinitionProvider>(MockBehavior.Strict);
        policyProvider
            .Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDefinition.Default);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        market
            .Setup(service => service.BuildSnapshotAsync(
                account.ExchangeId,
                position.ExchangePositionKey.InstrumentId.Value!,
                position.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiSnapshotTestData.CreateSnapshot());
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                marketSnapshotService: market.Object,
                exchangeAccountRepository: accountRepository.Object,
                portfolioStateRepository: portfolioRepository.Object,
                policyDefinitionProvider: policyProvider.Object));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("position_not_evaluable");
        problem.Extensions["reason"]!.ToString().Should().Be("temporalInconsistency");
    }

    [Fact]
    public async Task Post_maps_market_unavailable_to_stable_503_problem()
    {
        var userId = UserId.New();
        var position = CreatePosition();
        var account = ExchangeAccount.Create(
            position.ExchangePositionKey.ExchangeAccountId,
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        accountRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                userId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        var policyProvider = new Mock<IRecommendationPolicyDefinitionProvider>(MockBehavior.Strict);
        policyProvider
            .Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDefinition.Default);
        var market = new Mock<IMarketSnapshotService>(MockBehavior.Strict);
        market
            .Setup(service => service.BuildSnapshotAsync(
                account.ExchangeId,
                position.ExchangePositionKey.InstrumentId.Value!,
                position.MarketCategory,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MarketDataUnavailableException("market unavailable"));
        var positionRepository = CreatePositionRepository(userId, position);
        using var client = CreateClient(
            userId,
            CreateService(
                positionRepository.Object,
                marketSnapshotService: market.Object,
                exchangeAccountRepository: accountRepository.Object,
                portfolioStateRepository: portfolioRepository.Object,
                policyDefinitionProvider: policyProvider.Object));

        using var response = await client.PostAsync(
            $"/api/v1/positions/{position.Id.Value}/evaluation",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("market_data_unavailable");
    }

    [Fact]
    public async Task Get_hides_missing_position_as_not_found()
    {
        var userId = UserId.New();
        var positionRepository = new Mock<IPositionRepository>(MockBehavior.Strict);
        positionRepository
            .Setup(repository => repository.GetByIdAsync(
                userId,
                It.IsAny<PositionId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<Position>?)null);
        using var client = CreateClient(
            userId,
            CreateService(positionRepository.Object));

        using var response = await client.GetAsync(
            $"/api/v1/positions/{Guid.NewGuid()}/evaluation");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("resource_not_found");
    }

    private HttpClient CreateClient(
        UserId userId,
        PositionEvaluationService service)
    {
        var clientFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PositionEvaluationService>();
                services.AddSingleton(service);
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

        var client = clientFactory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.Value.ToString());
        return client;
    }

    private static PositionEvaluationService CreateService(
        IPositionRepository positionRepository,
        IPositionAssessmentRepository? assessmentRepository = null,
        IRecommendationRepository? recommendationRepository = null,
        IMarketSnapshotService? marketSnapshotService = null,
        IExchangeAccountRepository? exchangeAccountRepository = null,
        IPortfolioStateRepository? portfolioStateRepository = null,
        IRecommendationPolicyDefinitionProvider? policyDefinitionProvider = null,
        RecommendationService? recommendationService = null) =>
        new(
            positionRepository,
            exchangeAccountRepository ?? new Mock<IExchangeAccountRepository>(MockBehavior.Strict).Object,
            portfolioStateRepository ?? new Mock<IPortfolioStateRepository>(MockBehavior.Strict).Object,
            assessmentRepository ?? new Mock<IPositionAssessmentRepository>(MockBehavior.Strict).Object,
            recommendationRepository ?? new Mock<IRecommendationRepository>(MockBehavior.Strict).Object,
            marketSnapshotService ?? new Mock<IMarketSnapshotService>(MockBehavior.Strict).Object,
            policyDefinitionProvider ??
                new Mock<IRecommendationPolicyDefinitionProvider>(MockBehavior.Strict).Object,
            new PositionAssessmentService(),
            recommendationService ?? null!,
            new InlineEvaluationTransaction(),
            new InlineEvaluationOutbox(),
            new PositionEvaluationPolicySettings(
                PositionAssessmentRules.Default,
                new PortfolioRiskPolicySettings(20m, 200m, 50m)),
            new FixedTimeProvider(T0.AddMinutes(2)));

    private static RecommendationService CreateRecommendationService(
        IRecommendationPolicyDefinitionProvider policyProvider,
        IRecommendationRepository recommendationRepository,
        IRecommendationStabilityStateRepository stabilityStateRepository,
        IRecommendationPublicationTransaction publicationTransaction,
        IPositionAssessmentRepository assessmentRepository) =>
        new(
            policyProvider,
            new RecommendationPolicy(),
            new RecommendationStabilityPolicy(),
            recommendationRepository,
            stabilityStateRepository,
            publicationTransaction,
            assessmentRepository);

    private sealed class InlineEvaluationTransaction : IPositionEvaluationTransaction
    {
        public Task ExecuteAsync(
            UserId userId,
            PositionId positionId,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class InlineEvaluationOutbox : IApplicationEventOutbox
    {
        public Task AddAsync(
            IApplicationEvent applicationEvent,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddRangeAsync(
            IReadOnlyCollection<IApplicationEvent> applicationEvents,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static Mock<IPositionRepository> CreatePositionRepository(
        UserId userId,
        Position position)
    {
        var repository = new Mock<IPositionRepository>(MockBehavior.Strict);
        repository
            .Setup(value => value.GetByIdAsync(
                userId,
                position.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<Position>(position, ConcurrencyVersion.Initial));
        return repository;
    }

    private static Position CreatePosition()
    {
        var accountId = ExchangeAccountId.New();
        return Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            markPrice: 105m,
            unrealizedPnl: 5m);
    }

    private static async Task AssertValidationProblem(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType
            .Should()
            .Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Type.Should().Be("urn:intelligence-trade:error:validation-failed");
        problem.Title.Should().Be("Request validation failed.");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Detail.Should().NotBeNullOrWhiteSpace();
        problem.Instance.Should().NotBeNullOrWhiteSpace();
        problem.Extensions["code"]!.ToString().Should().Be("validation_failed");
        problem.Extensions["traceId"]!.ToString().Should().NotBeNullOrWhiteSpace();
    }

    private static PositionAssessment CreateLegacyAssessment(Position position) =>
        PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                position.Id,
                position.ExchangePositionKey.ExchangeAccountId,
                position.ExchangePositionKey.InstrumentId,
                position.LastObservedAt,
                T0,
                T0),
            new RuleVersion("assessment-v1"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            T0.AddMinutes(1),
            T0.AddMinutes(6));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
