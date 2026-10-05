using Intelligence.TradeSystem.Bff.Authentication;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class RefreshGateRegistryTests
{
    private readonly RefreshGateRegistry registry = new();

    [Fact]
    public void Single_lease_is_removed_after_release()
    {
        var gate = registry.Acquire("user-1");

        registry.Count.Should().Be(1);
        registry.UsersOf("user-1").Should().Be(1);

        registry.Release("user-1", gate);

        registry.UsersOf("user-1").Should().Be(0);
        registry.Count.Should().Be(0);
    }

    [Fact]
    public void Owner_and_waiter_share_one_gate_until_last_release()
    {
        var owner = registry.Acquire("user-1");
        var waiter = registry.Acquire("user-1");

        waiter.Should().BeSameAs(owner);
        registry.UsersOf("user-1").Should().Be(2);

        registry.Release("user-1", owner);

        registry.Count.Should().Be(1);
        registry.UsersOf("user-1").Should().Be(1);

        registry.Release("user-1", waiter);

        registry.Count.Should().Be(0);
    }

    [Fact]
    public void Gate_acquired_after_cleanup_is_new_instance()
    {
        var first = registry.Acquire("user-1");
        registry.Release("user-1", first);

        var second = registry.Acquire("user-1");

        second.Should().NotBeSameAs(first);
        registry.UsersOf("user-1").Should().Be(1);
        registry.Release("user-1", second);
        registry.Count.Should().Be(0);
    }

    [Fact]
    public void Releasing_another_gate_instance_does_not_remove_current_gate_of_subject()
    {
        var current = registry.Acquire("user-1");
        var stale = new RefreshGate { Users = 1 };

        registry.Release("user-1", stale);

        registry.Count.Should().Be(1);
        registry.UsersOf("user-1").Should().Be(1);
        registry.Acquire("user-1").Should().BeSameAs(current);
    }

    [Fact]
    public void Different_subjects_get_independent_gates()
    {
        var first = registry.Acquire("user-1");
        var second = registry.Acquire("user-2");

        second.Should().NotBeSameAs(first);
        first.Semaphore.Wait(0).Should().BeTrue();
        second.Semaphore.Wait(0).Should().BeTrue("lock user-1 не блокирует user-2");

        first.Semaphore.Release();
        registry.Release("user-1", first);

        registry.Count.Should().Be(1);
        registry.UsersOf("user-2").Should().Be(1);
        second.Semaphore.Release();
        registry.Release("user-2", second);
        registry.Count.Should().Be(0);
    }
}
