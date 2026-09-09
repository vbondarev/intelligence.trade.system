using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class ApplicationEventOutboxPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Event_survives_context_restart_and_claim_is_exclusive_until_lease_expiry()
    {
        var applicationEvent = new ExchangeAccountSyncDegradedEventV1(
            Guid.NewGuid(),
            CreatedAt,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ExchangeId.Bybit,
            "positions_partial",
            CreatedAt.AddMinutes(-1));

        await using (var cleanupContext = await CreateMigratedContext())
        {
            await cleanupContext.OutboxMessages.ExecuteDeleteAsync();
        }

        await using (var writeContext = await CreateMigratedContext())
        {
            await new ApplicationEventOutbox(writeContext, new FixedTimeProvider(CreatedAt))
                .AddAsync(applicationEvent);
        }

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstClaimTask = new ApplicationEventOutbox(firstContext).ClaimAsync(
            10,
            "dispatcher-a",
            CreatedAt.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var secondClaimTask = new ApplicationEventOutbox(secondContext).ClaimAsync(
            10,
            "dispatcher-b",
            CreatedAt.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var claims = await Task.WhenAll(firstClaimTask, secondClaimTask);

        Assert.Single(claims.SelectMany(value => value));
        var claim = claims.SelectMany(value => value).Single();
        Assert.Equal(applicationEvent.EventId, claim.EventId);

        await using var leaseContext = fixture.CreateContext();
        var reclaimed = await new ApplicationEventOutbox(leaseContext)
            .ClaimAsync(
                10,
                "dispatcher-c",
                CreatedAt.AddMinutes(7),
                TimeSpan.FromMinutes(5));
        Assert.Single(reclaimed);
        Assert.Equal(applicationEvent.EventId, reclaimed[0].EventId);

        await using var staleClaimContext = fixture.CreateContext();
        Assert.False(
            await new ApplicationEventOutbox(staleClaimContext)
                .MarkProcessedAsync(claim, CreatedAt.AddMinutes(7)));

        await using var retryContext = fixture.CreateContext();
        Assert.True(
            await new ApplicationEventOutbox(retryContext)
                .ScheduleRetryAsync(reclaimed[0], CreatedAt.AddMinutes(8)));

        await using var finalContext = fixture.CreateContext();
        var finalClaim = (await new ApplicationEventOutbox(finalContext)
                .ClaimAsync(
                    10,
                    "dispatcher-d",
                    CreatedAt.AddMinutes(9),
                    TimeSpan.FromMinutes(5)))
            .Single();
        Assert.True(
            await new ApplicationEventOutbox(finalContext)
                .MarkProcessedAsync(finalClaim, CreatedAt.AddMinutes(10)));

        await using var verificationContext = fixture.CreateContext();
        var persisted = await verificationContext.OutboxMessages
            .SingleAsync(message => message.EventId == applicationEvent.EventId);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal(CreatedAt.AddMinutes(10), persisted.ProcessedAt);
        Assert.Null(persisted.ClaimedBy);
        Assert.Equal("exchange-account.sync-degraded", persisted.EventType);
        Assert.Contains("positions_partial", persisted.Payload, StringComparison.Ordinal);
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
