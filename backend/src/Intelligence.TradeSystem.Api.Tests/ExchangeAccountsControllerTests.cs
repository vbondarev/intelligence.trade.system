using System.Net;
using System.Net.Http.Json;
using System.Text;
using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Tests.Support;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Регрессионное покрытие на уровне HTTP для жизненного цикла exchange-account
/// contract F-02, доступного по <c>/api/v1/exchange-accounts</c>. Тесты подменяют реальные
/// Application services строгими mocks, чтобы проверять маршрутизацию, model binding,
/// authorization и центральный <c>ApiExceptionHandler</c> на каждом задокументированном
/// сопоставлении исходов.
/// </summary>
public sealed class ExchangeAccountsControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ExchangeAccountsControllerTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task List_returns_only_the_current_users_accounts_in_an_items_envelope()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.ListActiveAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExchangeAccount>)[account]);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.GetAsync("/api/v1/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ExchangeAccountListResponse>(V1JsonSerializerOptions.Default);
        body!.Items.Should().ContainSingle(x => x.Id == account.Id.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task List_response_body_never_contains_credential_material()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.ListActiveAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExchangeAccount>)[account]);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.GetAsync("/api/v1/exchange-accounts");
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContainAny("apiKey", "apiSecret", "credential", "Credential");
    }

    [Fact]
    public async Task Unexpected_framework_exception_returns_safe_internal_error()
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.ListActiveAsync(userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("internal diagnostic detail"));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.GetAsync("/api/v1/exchange-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("internal diagnostic detail");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("internal_error");
        problem.Detail.Should().BeNull();
    }

    [Fact]
    public async Task Connect_returns_201_created_without_a_location_header()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.ConnectAsync(
                userId, ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountConnectionResult.Connected(account));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/exchange-accounts",
            new { exchange = "bybit", apiKey = "api-key", apiSecret = "api-secret" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().BeNull();
        service.VerifyAll();
    }

    [Fact]
    public async Task Connect_validates_required_fields_without_calling_the_application_layer()
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/exchange-accounts",
            new { exchange = "bybit", apiKey = "  ", apiSecret = "api-secret" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
        service.Verify(
            x => x.ConnectAsync(
                It.IsAny<UserId>(), It.IsAny<ExchangeId>(), It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Connect_returns_structured_validation_problem_for_malformed_json()
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsync(
            "/api/v1/exchange-accounts",
            new StringContent("{", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
        problem.Extensions["traceId"].Should().NotBeNull();
        problem.Extensions["errors"].Should().NotBeNull();
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ExchangeAccountConnectionOutcome.InvalidCredentials, HttpStatusCode.BadRequest)]
    [InlineData(ExchangeAccountConnectionOutcome.PermissionsRejected, HttpStatusCode.Forbidden)]
    [InlineData(ExchangeAccountConnectionOutcome.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Connect_maps_application_outcomes_to_the_documented_status_codes(
        ExchangeAccountConnectionOutcome outcome,
        HttpStatusCode expectedStatus)
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.ConnectAsync(
                userId, ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountConnectionResult.Failed(outcome));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/exchange-accounts",
            new { exchange = "bybit", apiKey = "api-key", apiSecret = "api-secret" });

        response.StatusCode.Should().Be(expectedStatus);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny("api-key", "api-secret");
        if (outcome == ExchangeAccountConnectionOutcome.PermissionsRejected)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            problem!.Extensions["code"]!.ToString().Should().Be("exchange_permissions_rejected");
        }
    }

    [Fact]
    public async Task Connect_propagates_the_authenticated_users_id_to_the_application_layer()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        UserId? captured = null;
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.ConnectAsync(
                It.IsAny<UserId>(), ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, ExchangeId, ExchangeAccountCredentialSecret, CancellationToken>(
                (uid, _, _, _) => captured = uid)
            .ReturnsAsync(ExchangeAccountConnectionResult.Connected(account));
        using var client = CreateClient(userId, service.Object);

        await client.PostAsJsonAsync(
            "/api/v1/exchange-accounts",
            new { exchange = "bybit", apiKey = "api-key", apiSecret = "api-secret" });

        captured.Should().Be(userId);
    }

    [Theory]
    [InlineData("verify", "POST")]
    [InlineData("credentials", "PUT")]
    [InlineData("sync", "POST")]
    [InlineData("", "DELETE")]
    public async Task Malformed_route_guid_returns_a_400_validation_problem(string suffix, string method)
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        var sync = new Mock<IExchangeAccountSyncService>(MockBehavior.Strict);
        using var client = CreateClient(userId, service.Object, sync.Object);
        var path = string.IsNullOrEmpty(suffix)
            ? "/api/v1/exchange-accounts/not-a-guid"
            : $"/api/v1/exchange-accounts/not-a-guid/{suffix}";

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(new { apiKey = "api-key", apiSecret = "api-secret" });

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Verify_returns_200_on_success()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var account = CreateAccount(userId, accountId);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.VerifyAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountVerificationResult(ExchangeAccountVerificationOutcome.Succeeded, account));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsync($"/api/v1/exchange-accounts/{accountId.Value}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.VerifyAll();
    }

    [Theory]
    [InlineData(ExchangeAccountVerificationOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ExchangeAccountVerificationOutcome.AccountDisabled, HttpStatusCode.Conflict)]
    [InlineData(ExchangeAccountVerificationOutcome.ProviderIdentityMismatch, HttpStatusCode.Conflict)]
    [InlineData(ExchangeAccountVerificationOutcome.InvalidCredentials, HttpStatusCode.BadRequest)]
    [InlineData(ExchangeAccountVerificationOutcome.PermissionsRejected, HttpStatusCode.Forbidden)]
    [InlineData(ExchangeAccountVerificationOutcome.CredentialsUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ExchangeAccountVerificationOutcome.ExchangeUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Verify_maps_every_application_outcome_to_the_documented_status_code(
        ExchangeAccountVerificationOutcome outcome,
        HttpStatusCode expectedStatus)
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.VerifyAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountVerificationResult(outcome, null));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsync($"/api/v1/exchange-accounts/{accountId.Value}/verify", null);

        response.StatusCode.Should().Be(expectedStatus);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny("Bybit", "bybit-provider", "provider-secret-like-message");
    }

    [Fact]
    public async Task Verify_missing_and_foreign_accounts_return_an_identical_resource_not_found_body()
    {
        var userId = UserId.New();
        var missingId = ExchangeAccountId.New();
        var foreignId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.VerifyAsync(userId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountVerificationResult(ExchangeAccountVerificationOutcome.NotFound, null));
        service.Setup(x => x.VerifyAsync(userId, foreignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountVerificationResult(ExchangeAccountVerificationOutcome.NotFound, null));
        using var client = CreateClient(userId, service.Object);

        using var missingResponse = await client.PostAsync($"/api/v1/exchange-accounts/{missingId.Value}/verify", null);
        using var foreignResponse = await client.PostAsync($"/api/v1/exchange-accounts/{foreignId.Value}/verify", null);

        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missingProblem = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var foreignProblem = await foreignResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        missingProblem!.Type.Should().Be("urn:intelligence-trade:error:resource-not-found");
        missingProblem.Extensions["code"]!.ToString().Should().Be("resource_not_found");
        foreignProblem!.Type.Should().Be(missingProblem.Type);
        foreignProblem.Title.Should().Be(missingProblem.Title);
        foreignProblem.Detail.Should().Be(missingProblem.Detail);
        foreignProblem.Extensions["code"]!.ToString().Should().Be(missingProblem.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task Verify_concurrency_conflict_returns_409()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.VerifyAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("stale version"));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsync($"/api/v1/exchange-accounts/{accountId.Value}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions["code"]!.ToString().Should().Be("concurrency_conflict");
    }

    [Fact]
    public async Task Rotate_returns_200_on_success()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var account = CreateAccount(userId, accountId);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.RotateCredentialsAsync(
                userId, accountId, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialRotationResult(
                ExchangeAccountCredentialRotationOutcome.Succeeded, account));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/credentials",
            new { apiKey = "new-key", apiSecret = "new-secret" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.VerifyAll();
    }

    [Theory]
    [InlineData(ExchangeAccountCredentialRotationOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ExchangeAccountCredentialRotationOutcome.AccountDisabled, HttpStatusCode.Conflict)]
    [InlineData(ExchangeAccountCredentialRotationOutcome.ProviderIdentityMismatch, HttpStatusCode.Conflict)]
    [InlineData(ExchangeAccountCredentialRotationOutcome.InvalidCredentials, HttpStatusCode.BadRequest)]
    [InlineData(ExchangeAccountCredentialRotationOutcome.PermissionsRejected, HttpStatusCode.Forbidden)]
    [InlineData(ExchangeAccountCredentialRotationOutcome.CredentialsUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ExchangeAccountCredentialRotationOutcome.ExchangeUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Rotate_maps_every_application_outcome_to_the_documented_status_code(
        ExchangeAccountCredentialRotationOutcome outcome,
        HttpStatusCode expectedStatus)
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.RotateCredentialsAsync(
                userId, accountId, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialRotationResult(outcome, null));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/credentials",
            new { apiKey = "new-key", apiSecret = "new-secret" });

        response.StatusCode.Should().Be(expectedStatus);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainAny("new-key", "new-secret");
    }

    [Fact]
    public async Task Rotate_identity_mismatch_returns_the_stable_problem_details_code()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.RotateCredentialsAsync(
                userId, accountId, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialRotationResult(
                ExchangeAccountCredentialRotationOutcome.ProviderIdentityMismatch, null));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/credentials",
            new { apiKey = "new-key", apiSecret = "new-secret" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be("urn:intelligence-trade:error:exchange-account-identity-mismatch");
        problem.Extensions["code"]!.ToString().Should().Be("exchange_account_identity_mismatch");
    }

    [Fact]
    public async Task Rotate_concurrency_conflict_returns_409()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.RotateCredentialsAsync(
                userId, accountId, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("stale version"));
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/exchange-accounts/{accountId.Value}/credentials",
            new { apiKey = "new-key", apiSecret = "new-secret" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(ExchangeAccountSyncOutcome.Synchronized, HttpStatusCode.OK)]
    [InlineData(ExchangeAccountSyncOutcome.AlreadyApplied, HttpStatusCode.OK)]
    [InlineData(ExchangeAccountSyncOutcome.Superseded, HttpStatusCode.OK)]
    [InlineData(ExchangeAccountSyncOutcome.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ExchangeAccountSyncOutcome.AccountDisabled, HttpStatusCode.Conflict)]
    [InlineData(ExchangeAccountSyncOutcome.CredentialsUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ExchangeAccountSyncOutcome.ExchangeUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Sync_maps_every_application_outcome_to_the_documented_status_code(
        ExchangeAccountSyncOutcome outcome,
        HttpStatusCode expectedStatus)
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var account = CreateAccount(userId, accountId);
        var result = outcome switch
        {
            ExchangeAccountSyncOutcome.Synchronized => ExchangeAccountSyncResult.Synchronized(
                account,
                PortfolioState.Create(
                    accountId, [], new PortfolioCapitalState(100m, 80m, DateTimeOffset.UtcNow, 100m),
                    DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5))),
            ExchangeAccountSyncOutcome.AlreadyApplied => ExchangeAccountSyncResult.AlreadyApplied(account),
            ExchangeAccountSyncOutcome.Superseded => ExchangeAccountSyncResult.Superseded(account),
            ExchangeAccountSyncOutcome.NotFound => ExchangeAccountSyncResult.NotFound(),
            ExchangeAccountSyncOutcome.AccountDisabled => ExchangeAccountSyncResult.AccountDisabled(),
            ExchangeAccountSyncOutcome.CredentialsUnavailable => ExchangeAccountSyncResult.CredentialsUnavailable(),
            _ => ExchangeAccountSyncResult.ExchangeUnavailable(),
        };
        var sync = new Mock<IExchangeAccountSyncService>(MockBehavior.Strict);
        sync.Setup(x => x.SynchronizeAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        using var client = CreateClient(userId, new Mock<IExchangeAccountService>(MockBehavior.Strict).Object, sync.Object);

        using var response = await client.PostAsync($"/api/v1/exchange-accounts/{accountId.Value}/sync", null);

        response.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Disconnect_returns_204_on_success()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var account = CreateAccount(userId, accountId);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.DisconnectAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.DeleteAsync($"/api/v1/exchange-accounts/{accountId.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        service.VerifyAll();
    }

    [Fact]
    public async Task Disconnect_missing_and_foreign_accounts_return_an_identical_resource_not_found_body()
    {
        var userId = UserId.New();
        var missingId = ExchangeAccountId.New();
        var foreignId = ExchangeAccountId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service.Setup(x => x.DisconnectAsync(userId, missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccount?)null);
        service.Setup(x => x.DisconnectAsync(userId, foreignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccount?)null);
        using var client = CreateClient(userId, service.Object);

        using var missingResponse = await client.DeleteAsync($"/api/v1/exchange-accounts/{missingId.Value}");
        using var foreignResponse = await client.DeleteAsync($"/api/v1/exchange-accounts/{foreignId.Value}");

        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missingProblem = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var foreignProblem = await foreignResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        missingProblem!.Extensions["code"]!.ToString().Should().Be("resource_not_found");
        foreignProblem!.Extensions["code"]!.ToString().Should().Be(missingProblem.Extensions["code"]!.ToString());
        foreignProblem.Detail.Should().Be(missingProblem.Detail);
    }

    [Fact]
    public async Task Disconnect_malformed_guid_returns_400_not_404()
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.DeleteAsync("/api/v1/exchange-accounts/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pre_v1_exchange_account_routes_are_gone()
    {
        var userId = UserId.New();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        using var client = CreateClient(userId, service.Object);

        using var response = await client.PostAsJsonAsync(
            "/api/exchange-accounts/bybit",
            new { apiKey = "api-key", apiSecret = "api-secret" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient CreateClient(
        UserId userId,
        IExchangeAccountService service,
        IExchangeAccountSyncService? syncService = null)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExchangeAccountService>();
                services.AddSingleton(service);
                services.RemoveAll<IExchangeAccountSyncService>();
                services.AddSingleton(syncService ?? new Mock<IExchangeAccountSyncService>(MockBehavior.Strict).Object);
                services.RemoveAll<PortfolioReadService>();
                services.AddSingleton(new PortfolioReadService(
                    new Mock<IPortfolioReadStore>(MockBehavior.Strict).Object));

                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, _ => { });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.Value.ToString());
        return client;
    }

    private static ExchangeAccount CreateAccount(UserId userId, ExchangeAccountId? id = null) =>
        ExchangeAccount.Create(
            id ?? ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
}
