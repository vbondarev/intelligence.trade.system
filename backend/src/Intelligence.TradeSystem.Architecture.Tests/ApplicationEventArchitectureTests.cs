using FluentAssertions;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class ApplicationEventArchitectureTests
{
    [Fact]
    public void Application_event_contracts_do_not_reference_ef_core()
    {
        typeof(IApplicationEvent).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should()
            .NotContain("Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void Outbox_persistence_remains_outside_the_application_event_contract_assembly()
    {
        Assert.NotSame(
            typeof(OutboxMessageEntity).Assembly,
            typeof(IApplicationEvent).Assembly);
    }
}
