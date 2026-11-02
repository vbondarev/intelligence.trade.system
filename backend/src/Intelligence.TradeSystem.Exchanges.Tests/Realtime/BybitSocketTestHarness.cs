using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using CryptoExchange.Net.Clients;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Testing;
using CryptoExchange.Net.Testing.Implementations;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets.Default;
using CryptoExchange.Net.Sockets.Default.Interfaces;
using CryptoExchange.Net.Sockets.HighPerf.Interfaces;
using Microsoft.Extensions.Logging;
using BybitNetCredentials = Bybit.Net.BybitCredentials;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

/// <summary>
/// Настоящий <see cref="BybitSocketClient"/> поверх подменённого websocket без сетевого подключения.
/// </summary>
internal sealed class BybitSocketTestHarness : IDisposable
{
    private readonly ConcurrentQueue<string> _sent = new();

    // Настоящий websocket доставляет входящие сообщения из одного receive loop, и разбор и маршрутизация
    // сообщений в CryptoExchange.Net не рассчитаны на конкурентные вызовы: одновременные ответы на
    // query теряются, и query ожидает таймаута SDK.
    private readonly Lock _receiveGate = new();

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
        ((SocketApiClient)Client.V5LinearApi).SocketFactory =
            new FaultInjectingWebsocketFactory(
                Socket,
                () => FailClose,
                () => Interlocked.Increment(ref _closeAttempts));

        AuthSucceeds = authSucceeds;
        SubscribeSucceeds = subscribeSucceeds;
        Socket.OnMessageSend += Respond;
    }

    public BybitSocketClient Client { get; }

    public TestSocket Socket { get; }

    public bool AuthSucceeds { get; set; }

    public bool SubscribeSucceeds { get; set; }

    public bool FailClose { get; set; }

    public int CloseAttempts => Volatile.Read(ref _closeAttempts);

    /// <summary>Topic, подписка на который отклоняется даже при <see cref="SubscribeSucceeds"/>.</summary>
    public string? RejectedTopic { get; set; }

    public IReadOnlyCollection<string> SentMessages => _sent.ToArray();

    public void Push(string message)
    {
        lock (_receiveGate)
        {
            Socket.InvokeMessage(message);
        }
    }

    public void Dispose() => Client.Dispose();

    private int _closeAttempts;

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
            Push(response);
        });
    }

    private static bool HasTopic(JsonElement root, string? topic) =>
        topic is not null
        && root.TryGetProperty("args", out var args)
        && args.ValueKind == JsonValueKind.Array
        && args.EnumerateArray().Any(arg => arg.ValueKind == JsonValueKind.String && arg.GetString() == topic);

    private sealed class FaultInjectingWebsocketFactory(
        TestSocket socket,
        Func<bool> failClose,
        Action onClose) : IWebsocketFactory
    {
        public IWebsocket CreateWebsocket(
            ILogger logger,
            SocketConnection connection,
            WebSocketParameters parameters)
        {
            socket.Connection = connection;
            return new FaultInjectingWebsocket(socket, failClose, onClose);
        }

        public IHighPerfWebsocket CreateHighPerfWebsocket(
            ILogger logger,
            WebSocketParameters parameters,
            PipeWriter pipeWriter) =>
            throw new NotImplementedException();
    }

    private sealed class FaultInjectingWebsocket(
        TestSocket socket,
        Func<bool> failClose,
        Action onClose) : IWebsocket
    {
        public event Func<Task> OnClose
        {
            add => socket.OnClose += value;
            remove => socket.OnClose -= value;
        }

        public event Func<int, Task> OnRequestSent
        {
            add => socket.OnRequestSent += value;
            remove => socket.OnRequestSent -= value;
        }

        public event Func<int, Task>? OnRequestRateLimited
        {
            add => socket.OnRequestRateLimited += value;
            remove => socket.OnRequestRateLimited -= value;
        }

        public event Func<Task>? OnConnectRateLimited
        {
            add => socket.OnConnectRateLimited += value;
            remove => socket.OnConnectRateLimited -= value;
        }

        public event Func<Exception, Task> OnError
        {
            add => socket.OnError += value;
            remove => socket.OnError -= value;
        }

        public event Func<Task> OnOpen
        {
            add => socket.OnOpen += value;
            remove => socket.OnOpen -= value;
        }

        public event Func<Task> OnReconnecting
        {
            add => socket.OnReconnecting += value;
            remove => socket.OnReconnecting -= value;
        }

        public event Func<Task> OnReconnected
        {
            add => socket.OnReconnected += value;
            remove => socket.OnReconnected -= value;
        }

        public Func<Task<Uri?>>? GetReconnectionUrl
        {
            get => socket.GetReconnectionUrl;
            set => socket.GetReconnectionUrl = value;
        }

        public int Id => socket.Id;
        public double IncomingKbps => socket.IncomingKbps;
        public Uri Uri => socket.Uri;
        public bool IsClosed => socket.IsClosed;
        public bool IsOpen => socket.IsOpen;
        public DateTime? LastReceiveTime => socket.LastReceiveTime;

        public Task<CallResult> ConnectAsync(CancellationToken cancellationToken) =>
            socket.ConnectAsync(cancellationToken);

        public bool Send(int id, string data, int weight) => socket.Send(id, data, weight);

        public bool Send(int id, byte[] data, int weight) => socket.Send(id, data, weight);

        public Task ReconnectAsync() => socket.ReconnectAsync();

        public Task CloseAsync()
        {
            onClose();
            if (failClose())
            {
                throw new InvalidOperationException("Test websocket close failed.");
            }

            return socket.CloseAsync();
        }

        public void Dispose() => socket.Dispose();

        public void UpdateProxy(ApiProxy? proxy) => socket.UpdateProxy(proxy);
    }
}
