using System.Net;
using System.Net.Http.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionTimelineControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PositionTimelineControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Get_maps_repeatable_type_filter_and_returns_an_opaque_cursor_page()
    {
        var userId = UserId.New();
        var positionId = PositionId.New();
        var evaluation = CreateEvaluation();
        var nextCursor = new PositionTimelineCursor(
            evaluation.EvaluatedAt,
            PositionTimelineItemKind.Evaluation,
            evaluation.Id.Value,
            null);
        var store = new Mock<IPositionTimelineReadStore>(MockBehavior.Strict);
        PositionTimelineQuery? capturedQuery = null;
        store.Setup(x => x.ReadCandidatesAsync(userId, It.IsAny<PositionTimelineQuery>(), It.IsAny<CancellationToken>()))
            .Callback<UserId, PositionTimelineQuery, CancellationToken>((_, query, _) => capturedQuery = query)
            .ReturnsAsync(new PositionTimelineCandidates(
                [],
                [PositionTimelineItem.ForEvaluation(evaluation)],
                []));
        using var client = CreateClient(userId, store.Object);

        using var response = await client.GetAsync(
            $"/api/v1/positions/{positionId.Value}/timeline?pageSize=2&type=evaluation&type=evaluation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CursorPage<PositionTimelineItemResponse>>(
            V1JsonSerializerOptions.Default);
        body!.Items.Should().ContainSingle();
        body.Items[0].Type.Should().Be(PositionTimelineItemTypeV1.Evaluation);
        body.Items[0].Evaluation!.Id.Should().Be(evaluation.Id.Value);
        body.Items[0].PositionChange.Should().BeNull();
        body.Items[0].Recommendation.Should().BeNull();
        body.HasMore.Should().BeFalse();
        body.NextCursor.Should().BeNull();

        capturedQuery.Should().NotBeNull();
        capturedQuery!.PositionId.Should().Be(positionId);
        capturedQuery.PageSize.Should().Be(2);
        capturedQuery.SelectedKinds.Should().Equal(PositionTimelineItemKind.Evaluation);
        store.VerifyAll();
    }

    [Theory]
    [InlineData("not-a-guid", "")]
    [InlineData("00000000-0000-0000-0000-000000000000", "")]
    [InlineData("11111111-1111-1111-1111-111111111111", "?pageSize=")]
    [InlineData("11111111-1111-1111-1111-111111111111", "?pageSize=0")]
    [InlineData("11111111-1111-1111-1111-111111111111", "?pageSize=101")]
    [InlineData("11111111-1111-1111-1111-111111111111", "?cursor=invalid")]
    [InlineData("11111111-1111-1111-1111-111111111111", "?type=")]
    [InlineData("11111111-1111-1111-1111-111111111111", "?type=unknown")]
    public async Task Get_rejects_invalid_wire_values_without_calling_application(
        string id,
        string query)
    {
        var store = new Mock<IPositionTimelineReadStore>(MockBehavior.Strict);
        using var client = CreateClient(UserId.New(), store.Object);

        using var response = await client.GetAsync($"/api/v1/positions/{id}/timeline{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
        store.Verify(
            x => x.ReadCandidatesAsync(
                It.IsAny<UserId>(),
                It.IsAny<PositionTimelineQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Get_hides_missing_and_foreign_positions_as_the_same_not_found_problem()
    {
        var userId = UserId.New();
        var store = new Mock<IPositionTimelineReadStore>(MockBehavior.Strict);
        store.Setup(x => x.ReadCandidatesAsync(
                userId,
                It.IsAny<PositionTimelineQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionTimelineCandidates?)null);
        using var client = CreateClient(userId, store.Object);

        using var missingResponse = await client.GetAsync($"/api/v1/positions/{Guid.NewGuid()}/timeline");
        using var foreignResponse = await client.GetAsync($"/api/v1/positions/{Guid.NewGuid()}/timeline");

        var missing = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var foreign = await foreignResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreign!.Type.Should().Be(missing!.Type);
        foreign.Detail.Should().Be(missing.Detail);
        foreign.Extensions["code"]!.ToString().Should().Be("resource_not_found");
    }

    [Fact]
    public async Task Get_requires_authentication()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/positions/{Guid.NewGuid()}/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClient(UserId userId, IPositionTimelineReadStore store)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PositionTimelineService>();
                services.AddSingleton(new PositionTimelineService(store));
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.Value.ToString());
        return client;
    }

    private static PositionTimelineEvaluation CreateEvaluation()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        return new PositionTimelineEvaluation(
            PositionAssessmentId.New(),
            now,
            now.AddMinutes(5),
            new RuleVersion("assessment-v1"),
            false,
            new PositionAssessmentDataQualityContext(
                AssessmentDataQuality.FreshCompleteReliable,
                AssessmentDataQuality.FreshCompleteReliable),
            RiskIncreaseDecision.Allowed,
            []);
    }
}
