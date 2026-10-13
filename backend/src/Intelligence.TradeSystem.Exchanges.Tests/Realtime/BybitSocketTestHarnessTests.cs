using FluentAssertions;
using static Intelligence.TradeSystem.Exchanges.Tests.Realtime.BybitRealtimeTestData;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitSocketTestHarnessTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PendingProbe = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Inbound_Messages_Are_Delivered_Sequentially_Like_Websocket_Receive_Loop()
    {
        using var harness = new BybitSocketTestHarness();
        var callback = new BlockingCallback();
        var delivered = 0;
        var subscription = await harness.Client.V5PrivateApi.SubscribeToPositionUpdatesAsync(_ =>
        {
            if (Interlocked.Increment(ref delivered) == 1)
            {
                callback.Block();
            }
        });
        subscription.Success.Should().BeTrue();

        var first = Task.Run(() => harness.Push(PositionMessage(PositionItem())));
        await callback.Entered.WaitAsync(CompletionTimeout);
        var second = Task.Run(() => harness.Push(PositionMessage(PositionItem(("size", "0.7")))));

        var completed = await Task.WhenAny(second, Task.Delay(PendingProbe));
        completed.Should().NotBeSameAs(second, "следующее сообщение ожидает, пока SDK не завершит обработку текущего");
        Volatile.Read(ref delivered).Should().Be(1);

        callback.Release();
        await Task.WhenAll(first, second).WaitAsync(CompletionTimeout);
        Volatile.Read(ref delivered).Should().Be(2);
    }
}
