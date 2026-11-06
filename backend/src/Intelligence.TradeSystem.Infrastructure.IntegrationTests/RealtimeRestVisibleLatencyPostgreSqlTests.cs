using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Intelligence.TradeSystem.Api;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ApiProgram = Intelligence.TradeSystem.Api.Program;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql-A")]
public sealed class RealtimeRestVisibleLatencyPostgreSqlTests(
    PostgreSqlFixture fixture,
    ITestOutputHelper output)
{
    private const int SyntheticSampleCount = 20;
    private static readonly byte[] TestCredentialKey = new byte[32];

    [Fact]
    public async Task Private_position_observations_become_visible_through_rest_and_measure_nearest_rank_p95()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"latency-{Guid.NewGuid():N}"),
            "Latency test",
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        var connectedAt = DateTimeOffset.UtcNow;
        long generation;

        await using (var context = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(context)
                .SaveAsync(userId, account, expectedVersion: null);
            var stateStore = new RealtimeObservationStateStore(context);
            var state = await stateStore.AdvanceGenerationAsync(
                userId,
                account.Id,
                RealtimeObservationTransportStatus.Connected);
            generation = state.Value.Generation;
            await stateStore.SaveAsync(
                userId,
                state.Value with
                {
                    LastConnectedAt = connectedAt,
                    LastHeartbeatAt = connectedAt,
                    LastFullyReconciledAt = connectedAt,
                    RecoveryRequired = false,
                },
                state.Version);
        }

        using var factory = new TestApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserIdHeader,
            userId.Value.ToString("D"));
        var coordinator = factory.Services.GetRequiredService<ExchangeAccountObservationCoordinator>();
        var latencies = new List<double>(SyntheticSampleCount);
        DateTimeOffset? previousCreatedAt = null;

        for (var sequence = 1; sequence <= SyntheticSampleCount; sequence++)
        {
            var createdAt = DateTimeOffset.UtcNow;
            while (previousCreatedAt is { } previous && createdAt <= previous)
            {
                await Task.Delay(1);
                createdAt = DateTimeOffset.UtcNow;
            }

            var receivedAt = DateTimeOffset.UtcNow;
            var size = sequence;
            var observation = CreateObservation(account, createdAt, receivedAt, sequence, size);

            Assert.True(coordinator.TryEnqueue(userId, generation, observation));
            var results = await coordinator.ProcessPendingAsync(userId, account.Id);
            Assert.Equal(RealtimeObservationApplyOutcome.Applied, Assert.Single(results).Outcome);

            using var response = await client.GetAsync(
                $"/api/v1/positions?exchangeAccountId={account.Id.Value:D}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var visiblePosition = Assert.Single(
                items,
                item => item.GetProperty("symbol").GetString() == "BTCUSDT");
            Assert.Equal(size, visiblePosition.GetProperty("size").GetDecimal());

            var restVisibleAt = DateTimeOffset.UtcNow;
            Assert.True(restVisibleAt >= createdAt);
            latencies.Add((restVisibleAt - createdAt).TotalMilliseconds);
            previousCreatedAt = createdAt;
        }

        var orderedLatencies = latencies.OrderBy(latency => latency).ToArray();
        var p95Index = (int)Math.Ceiling(0.95 * orderedLatencies.Length) - 1;
        var p95Milliseconds = orderedLatencies[p95Index];
        output.WriteLine(
            $"Synthetic REST-visible samples={orderedLatencies.Length}; "
            + $"nearest-rank p95={p95Milliseconds:F2} ms; "
            + $"max={orderedLatencies[^1]:F2} ms.");
        Assert.InRange(p95Milliseconds, 0, TimeSpan.FromSeconds(5).TotalMilliseconds);
    }

    private static PrivatePositionObservation CreateObservation(
        ExchangeAccount account,
        DateTimeOffset createdAt,
        DateTimeOffset receivedAt,
        long sequence,
        decimal size)
    {
        var position = CreateOpenPosition(size);
        return new PrivatePositionObservation
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = account.Id,
            Kind = PrivatePositionObservationKind.Open,
            Symbol = position.Symbol,
            Category = MarketCategory.Linear,
            PositionIdx = position.PositionIdx,
            Side = position.Side,
            Size = position.Size,
            ProviderSequence = sequence,
            ProviderUpdatedAt = createdAt,
            MessageCreatedAt = createdAt,
            ReceivedAt = receivedAt,
            SettlementAsset = SettlementAsset.From("USDT"),
            Position = position,
        };
    }

    private static OpenPosition CreateOpenPosition(decimal size) =>
        new(
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            PositionStatus.Normal,
            size,
            100m,
            size * 100m,
            2m,
            100m,
            null,
            null,
            0m,
            null,
            null,
            null,
            1,
            null,
            null,
            null,
            0,
            SettlementAsset.From("USDT"));

    private sealed class TestApiFactory(string connectionString) : WebApplicationFactory<ApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TradeSystem", connectionString);
            builder.UseSetting("Authentication:Audience", "intelligence-trade-api");
            builder.UseSetting("CredentialProtection:ActiveKeyId", "test");
            builder.UseSetting(
                "CredentialProtection:Keys:test",
                Convert.ToBase64String(TestCredentialKey));
            builder.UseSetting("ApplicationEventOutboxDispatcher:Enabled", "false");
            builder.ConfigureTestServices(services =>
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { }));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RestVisibleTest";
        public const string UserIdHeader = "X-Test-User-Id";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserIdHeader, out var values)
                || !Guid.TryParse(values.ToString(), out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("sub", userId.ToString("D")),
                new Claim("trade_principal_type", "user"),
                new Claim("scope", "trade.api"),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
