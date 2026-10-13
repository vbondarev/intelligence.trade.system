using Bybit.Net.Interfaces.Clients;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using static Intelligence.TradeSystem.Exchanges.Tests.Realtime.BybitRealtimeTestData;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitRealtimeProviderFactoryTests
{
    [Fact]
    public async Task Create_Uses_Separate_Client_With_Account_Credentials_For_Each_Account()
    {
        var created = new List<(BybitCredentials Credentials, Mock<IBybitSocketClient> Client)>();
        var factory = CreateFactory(credentials =>
        {
            var client = new Mock<IBybitSocketClient>();
            created.Add((credentials, client));
            return client.Object;
        });
        var firstAccount = ExchangeAccountId.New();
        var secondAccount = ExchangeAccountId.New();

        await using var first = factory.Create(ExchangeId.Bybit, firstAccount, Credential("first-key", "first-secret"));
        await using var second = factory.Create(ExchangeId.Bybit, secondAccount, Credential("second-key", "second-secret"));

        first.ExchangeAccountId.Should().Be(firstAccount);
        second.ExchangeAccountId.Should().Be(secondAccount);
        first.Should().NotBeSameAs(second);
        created.Should().HaveCount(2);
        created[0].Credentials.ApiKey.Should().Be("first-key");
        created[0].Credentials.ApiSecret.Should().Be("first-secret");
        created[1].Credentials.ApiKey.Should().Be("second-key");
        created[0].Client.Should().NotBeSameAs(created[1].Client);
    }

    [Fact]
    public void Create_Rejects_Unsupported_Exchange_Without_Creating_Client()
    {
        var calls = 0;
        var factory = CreateFactory(_ =>
        {
            calls++;
            return new Mock<IBybitSocketClient>().Object;
        });

        var act = () => factory.Create((ExchangeId)999, ExchangeAccountId.New(), Credential());

        act.Should().Throw<NotSupportedException>();
        calls.Should().Be(0);
    }

    [Fact]
    public void Create_Rejects_Uninitialized_Account_Without_Creating_Client()
    {
        var calls = 0;
        var factory = CreateFactory(_ =>
        {
            calls++;
            return new Mock<IBybitSocketClient>().Object;
        });

        var act = () => factory.Create(ExchangeId.Bybit, default, Credential());

        act.Should().Throw<ArgumentException>();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Lease_Disposes_Owned_Client_Exactly_Once_And_Rejects_Start_After_Dispose()
    {
        var client = new Mock<IBybitSocketClient>();
        var factory = CreateFactory(_ => client.Object);
        var lease = factory.Create(ExchangeId.Bybit, ExchangeAccountId.New(), Credential());

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        client.Verify(socketClient => socketClient.Dispose(), Times.Once);
        var act = () => lease.StartAsync(new RecordingObserver());
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Lease_Dispose_Closes_Started_Subscriptions()
    {
        using var harness = new BybitSocketTestHarness();
        var factory = CreateFactory(_ => harness.Client);
        var observer = new RecordingObserver();
        var lease = factory.Create(ExchangeId.Bybit, ExchangeAccountId.New(), Credential());
        (await lease.StartAsync(observer)).Status.Should().Be(RealtimeTransportStatus.Subscribed);

        await lease.DisposeAsync();

        await WaitUntilAsync(() => !harness.Socket.IsOpen);
        observer.Positions.Should().BeEmpty();
        observer.TransportStates.Should().BeEmpty();
    }

    [Fact]
    public void CreatePrivateSocketClient_Sets_Credentials_And_Original_Data_For_Each_Client()
    {
        using var first = BybitClientFactory.CreatePrivateSocketClient(new BybitCredentials("first-key", "first-secret"));
        using var second = BybitClientFactory.CreatePrivateSocketClient(new BybitCredentials("second-key", "second-secret"));

        first.Should().NotBeSameAs(second);
        var options = GetClientOptions(first);
        options.GetType().GetProperty("ApiCredentials")!.GetValue(options).Should().NotBeNull();
        options.GetType().GetProperty("OutputOriginalData")!.GetValue(options).Should().Be(true);
    }

    [Fact]
    public void AddBybitExchange_Registers_Realtime_Factory_Without_Starting_Observation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        serviceProvider.GetRequiredService<IExchangeRealtimeProviderFactory>()
            .Should().BeOfType<BybitRealtimeProviderFactory>();
        serviceProvider.GetRequiredService<BybitInstrumentSettlementResolver>()
            .Should().BeSameAs(serviceProvider.GetRequiredService<BybitInstrumentSettlementResolver>());
        serviceProvider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        serviceProvider.GetService<IExchangeRealtimeProvider>().Should().BeNull();
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType.FullName == "Microsoft.Extensions.Hosting.IHostedService");
    }

    private static BybitRealtimeProviderFactory CreateFactory(Func<BybitCredentials, IBybitSocketClient> clientFactory) =>
        new(
            InstrumentMetadata.Resolver(("BTCUSDT", "USDT")),
            LoggerFactory.Create(_ => { }),
            TimeProvider.System,
            clientFactory);

    private static ExchangeAccountCredential Credential(string apiKey = "test-key", string apiSecret = "test-secret") =>
        new(new ExchangeAccountCredentialSecret(apiKey, apiSecret), ConcurrencyVersion.Initial);

    private static object GetClientOptions(IBybitSocketClient client) =>
        client.V5PrivateApi.GetType()
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(property => property.Name == "ClientOptions")
            ?.GetValue(client.V5PrivateApi)
        ?? throw new InvalidOperationException("Bybit socket client options were not found.");
}
