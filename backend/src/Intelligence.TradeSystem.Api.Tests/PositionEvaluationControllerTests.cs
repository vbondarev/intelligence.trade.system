using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Evaluations;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionEvaluationControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> factory;

    public PositionEvaluationControllerTests(WebApplicationFactory<Program> factory) =>
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
        market.Verify(
            service => service.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
        IMarketSnapshotService? marketSnapshotService = null) =>
        new(
            positionRepository,
            new Mock<IExchangeAccountRepository>(MockBehavior.Strict).Object,
            new Mock<IPortfolioStateRepository>(MockBehavior.Strict).Object,
            assessmentRepository ?? new Mock<IPositionAssessmentRepository>(MockBehavior.Strict).Object,
            recommendationRepository ?? new Mock<IRecommendationRepository>(MockBehavior.Strict).Object,
            marketSnapshotService ?? new Mock<IMarketSnapshotService>(MockBehavior.Strict).Object,
            new Mock<IRecommendationPolicyDefinitionProvider>(MockBehavior.Strict).Object,
            new PositionAssessmentService(),
            null!,
            new PositionEvaluationPolicySettings(
                PositionAssessmentRules.Default,
                new PortfolioRiskPolicySettings(20m, 200m, 50m)),
            new FixedTimeProvider(T0.AddMinutes(2)));

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
