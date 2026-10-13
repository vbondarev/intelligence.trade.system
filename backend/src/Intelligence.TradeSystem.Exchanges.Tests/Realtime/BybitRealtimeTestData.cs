using System.Text.Json;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects.Sockets;
using FluentAssertions;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

/// <summary>
/// Сообщения потоков Bybit V5 и их разбор настоящим SDK без сетевого подключения.
/// </summary>
internal static class BybitRealtimeTestData
{
    public const long CreationTimeMs = 1697682317044;
    public const long UpdatedTimeMs = 1697682317038;
    public const long CreatedTimeMs = 1697682300000;
    public const long Sequence = 8172241650;

    /// <summary>
    /// Элемент <c>data</c> позиции; значение <see langword="null"/> в <paramref name="overrides"/> удаляет поле.
    /// </summary>
    public static string PositionItem(params (string Name, object? Value)[] overrides)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["positionIdx"] = 0,
            ["tradeMode"] = 0,
            ["riskId"] = 1,
            ["riskLimitValue"] = "2000000",
            ["symbol"] = "BTCUSDT",
            ["side"] = "Buy",
            ["size"] = "0.5",
            ["entryPrice"] = "30000",
            ["leverage"] = "10",
            ["positionValue"] = "15000",
            ["markPrice"] = "30100",
            ["unrealisedPnl"] = "50",
            ["createdTime"] = CreatedTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["updatedTime"] = UpdatedTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["category"] = "linear",
            ["positionStatus"] = "Normal",
            ["seq"] = Sequence,
        };

        foreach (var (name, value) in overrides)
        {
            if (value is null)
            {
                fields.Remove(name);
            }
            else
            {
                fields[name] = value;
            }
        }

        return JsonSerializer.Serialize(fields);
    }

    public static string PositionMessage(params string[] items) =>
        PrivateMessage("position", CreationTimeMs, items);

    public static string PrivateMessage(string topic, object? creationTime, params string[] items)
    {
        var creation = creationTime is null ? string.Empty : $",\"creationTime\":{JsonSerializer.Serialize(creationTime)}";
        return $"{{\"id\":\"test-message\",\"topic\":\"{topic}\"{creation},\"data\":[{string.Join(',', items)}]}}";
    }

    public static string WalletMessage(object? creationTime = null) =>
        PrivateMessage(
            "wallet",
            creationTime ?? CreationTimeMs,
            """{"accountType":"UNIFIED","totalEquity":"12500","totalWalletBalance":"12000","totalAvailableBalance":"8000","totalPerpUPL":"500","coin":[{"coin":"USDT","equity":"12500","usdValue":"12500","walletBalance":"12000"}]}""",
            """{"accountType":"FUND","totalEquity":"1"}""",
            """{"accountType":"CONTRACT","totalEquity":"7"}""");

    public static async Task<DataEvent<BybitPositionUpdate[]>> CapturePositionsAsync(string message)
    {
        using var harness = new BybitSocketTestHarness();
        DataEvent<BybitPositionUpdate[]>? captured = null;
        var subscription = await harness.Client.V5PrivateApi.SubscribeToPositionUpdatesAsync(update => captured = update);
        subscription.Success.Should().BeTrue();

        harness.Push(message);

        return captured ?? throw new InvalidOperationException("SDK did not deliver the position message.");
    }

    public static async Task<DataEvent<BybitBalance[]>> CaptureWalletAsync(string message)
    {
        using var harness = new BybitSocketTestHarness();
        DataEvent<BybitBalance[]>? captured = null;
        var subscription = await harness.Client.V5PrivateApi.SubscribeToWalletUpdatesAsync(update => captured = update);
        subscription.Success.Should().BeTrue();

        harness.Push(message);

        return captured ?? throw new InvalidOperationException("SDK did not deliver the wallet message.");
    }

    public static async Task<IReadOnlyList<DataEvent<BybitLinearTickerUpdate>>> CaptureTickersAsync(params string[] messages)
    {
        using var harness = new BybitSocketTestHarness(authenticated: false);
        var captured = new List<DataEvent<BybitLinearTickerUpdate>>();
        var subscription = await harness.Client.V5LinearApi.SubscribeToTickerUpdatesAsync("BTCUSDT", captured.Add);
        subscription.Success.Should().BeTrue();

        foreach (var message in messages)
        {
            harness.Push(message);
        }

        return captured;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not satisfied in time.");
            }

            await Task.Delay(20);
        }
    }
}
