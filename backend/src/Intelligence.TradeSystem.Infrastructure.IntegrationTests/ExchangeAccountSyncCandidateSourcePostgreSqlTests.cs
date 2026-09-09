using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class ExchangeAccountSyncCandidateSourcePostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Returns_connected_and_unavailable_accounts_but_excludes_disabled_and_unknown()
    {
        var activeAccounts = Enumerable.Range(0, 5)
            .Select(index => ExchangeAccount.Create(
                ExchangeAccountId.New(),
                UserId.New(),
                ExchangeId.Bybit,
                index == 0
                    ? ExchangeAccountConnectionStatus.Unavailable
                    : ExchangeAccountConnectionStatus.Connected,
                ExchangeAccountCapabilities.ReadBalance |
                ExchangeAccountCapabilities.ReadPositions))
            .ToArray();
        var disabled = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Disabled);
        var unknown = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Unknown);

        await using (var setupContext = await CreateMigratedContext())
        {
            var repository = new ExchangeAccountRepository(setupContext);
            foreach (var account in activeAccounts.Append(disabled).Append(unknown))
            {
                await repository.SaveAsync(account.UserId, account, expectedVersion: null);
            }
        }

        var candidates = await ReadAllCandidatesAsync(batchSize: 2);
        var ownIds = activeAccounts
            .Select(account => account.Id)
            .Append(disabled.Id)
            .Append(unknown.Id)
            .ToHashSet();
        var ownCandidates = candidates
            .Where(candidate => ownIds.Contains(candidate.ExchangeAccountId))
            .ToArray();

        Assert.Equal(activeAccounts.Length, ownCandidates.Length);
        Assert.Equal(
            activeAccounts.Select(account => account.Id).Order(),
            ownCandidates.Select(candidate => candidate.ExchangeAccountId).Order());
        Assert.DoesNotContain(
            ownCandidates,
            candidate => candidate.ExchangeAccountId == disabled.Id);
        Assert.DoesNotContain(
            ownCandidates,
            candidate => candidate.ExchangeAccountId == unknown.Id);
        Assert.Equal(
            activeAccounts.Select(account => account.UserId).Order(),
            ownCandidates.Select(candidate => candidate.UserId).Order());
        Assert.Equal(
            candidates.Select(candidate => candidate.ExchangeAccountId).Distinct().Count(),
            candidates.Count);
        Assert.Equal(
            candidates.Select(candidate => candidate.ExchangeAccountId),
            candidates.Select(candidate => candidate.ExchangeAccountId).Order());
    }

    private async Task<IReadOnlyList<ExchangeAccountSyncCandidate>> ReadAllCandidatesAsync(
        int batchSize)
    {
        await using var context = await CreateMigratedContext();
        var source = new ExchangeAccountSyncCandidateSource(context);
        var all = new List<ExchangeAccountSyncCandidate>();
        ExchangeAccountId? after = null;

        while (true)
        {
            var batch = await source.GetBatchAsync(after, batchSize);
            if (batch.Count == 0)
            {
                return all;
            }

            all.AddRange(batch);
            after = batch[^1].ExchangeAccountId;
        }
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }
}
