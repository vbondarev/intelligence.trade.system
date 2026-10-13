using System.Collections.Concurrent;
using System.Text.Json;
using Bybit.Net.Clients;
using CryptoExchange.Net.Testing;
using CryptoExchange.Net.Testing.Implementations;
using BybitNetCredentials = Bybit.Net.BybitCredentials;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

/// <summary>
/// Настоящий <see cref="BybitSocketClient"/> поверх подменённого websocket без сетевого подключения.
/// </summary>
internal sealed class BybitSocketTestHarness : IDisposable
{
    private readonly ConcurrentQueue<string> _sent = new();

    public BybitSocketTestHarness(
        bool authenticated = true,
        bool authSucceeds = true,
        bool subscribeSucceeds = true,
        string apiKey = "test-api-key-value",
        string apiSecret = "test-api-secret-value")
    {
        Client = new BybitSocketClient(options =>
        {
            if (authenticated)
            {
                options.ApiCredentials = new BybitNetCredentials(apiKey, apiSecret);
            }

            options.OutputOriginalData = true;
        });
        // SDK выбирает формат auth-запроса по пути соединения, поэтому адрес совпадает с реальным приватным endpoint.
        Socket = TestHelpers.ConfigureSocketClient(
            Client,
            authenticated ? "wss://stream.bybit.com/v5/private" : "wss://stream.bybit.com/v5/public/linear");
        AuthSucceeds = authSucceeds;
        SubscribeSucceeds = subscribeSucceeds;
        Socket.OnMessageSend += Respond;
    }

    public BybitSocketClient Client { get; }

    public TestSocket Socket { get; }

    public bool AuthSucceeds { get; set; }

    public bool SubscribeSucceeds { get; set; }

    /// <summary>Topic, подписка на который отклоняется даже при <see cref="SubscribeSucceeds"/>.</summary>
    public string? RejectedTopic { get; set; }

    public IReadOnlyCollection<string> SentMessages => _sent.ToArray();

    public void Push(string message) => Socket.InvokeMessage(message);

    public void Dispose() => Client.Dispose();

    private void Respond(string message)
    {
        _sent.Enqueue(message);
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (!root.TryGetProperty("op", out var opElement)
            || !(root.TryGetProperty("req_id", out var requestIdElement)
                || root.TryGetProperty("reqId", out requestIdElement)))
        {
            return;
        }

        var op = opElement.GetString();
        var requestId = requestIdElement.GetString();
        var success = op switch
        {
            "auth" => AuthSucceeds,
            "subscribe" => SubscribeSucceeds && !HasTopic(root, RejectedTopic),
            _ => true,
        };
        var response = success
            ? $$"""{"success":true,"ret_msg":"","op":"{{op}}","conn_id":"test-connection","req_id":"{{requestId}}"}"""
            : $$"""{"success":false,"ret_msg":"10003: Invalid apikey","op":"{{op}}","conn_id":"test-connection","req_id":"{{requestId}}"}""";

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            Socket.InvokeMessage(response);
        });
    }

    private static bool HasTopic(JsonElement root, string? topic) =>
        topic is not null
        && root.TryGetProperty("args", out var args)
        && args.ValueKind == JsonValueKind.Array
        && args.EnumerateArray().Any(arg => arg.ValueKind == JsonValueKind.String && arg.GetString() == topic);
}
