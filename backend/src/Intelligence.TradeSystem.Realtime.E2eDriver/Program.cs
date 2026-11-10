using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Realtime.E2eDriver;

public static class Program
{
    public static async Task Main()
    {
        if (Environment.GetEnvironmentVariable("REALTIME_E2E_ENABLED") != "true")
            throw new InvalidOperationException("Realtime E2E driver разрешён только в test profile.");
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__TradeSystem");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Отсутствует test database.");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("REALTIME_E2E_USER_ID"), out var userId) || userId == Guid.Empty
            || !Guid.TryParse(Environment.GetEnvironmentVariable("REALTIME_E2E_ACCOUNT_ID"), out var accountId) || accountId == Guid.Empty)
            throw new InvalidOperationException("Требуются явные test UserId и ExchangeAccountId.");
        var name = Environment.GetEnvironmentVariable("REALTIME_E2E_NAME");
        if (name is not ("G07 initial" or "G07 changed" or "G07 recovered" or "cleanup"))
            throw new InvalidOperationException("Неизвестное test-состояние.");

        var options = new DbContextOptionsBuilder<TradeSystemDbContext>().UseNpgsql(connection).Options;
        await using var db = new TradeSystemDbContext(options);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var account = await db.ExchangeAccounts.SingleOrDefaultAsync(item => item.Id == accountId);
        if (name == "cleanup")
        {
            if (account is not null && account.UserId == userId
                && account.ProviderAccountId == "g07-test-" + accountId.ToString("N"))
            {
                db.ExchangeAccounts.Remove(account);
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return;
        }

        var displayName = $"{name} {accountId.ToString("N")[..8]}";
        if (account is null)
        {
            account = new ExchangeAccountEntity
            {
                Id = accountId,
                UserId = userId,
                ExchangeId = ExchangeId.Bybit,
                ProviderAccountId = "g07-test-" + accountId.ToString("N"),
                DisplayName = displayName,
                ConnectionStatus = ExchangeAccountConnectionStatus.Disabled,
                Capabilities = ExchangeAccountCapabilities.None,
                Version = 1,
            };
            db.ExchangeAccounts.Add(account);
        }
        else
        {
            if (account.UserId != userId || account.ProviderAccountId != "g07-test-" + accountId.ToString("N"))
                throw new InvalidOperationException("Тестовый аккаунт не принадлежит заданному пользователю.");
            account.DisplayName = displayName;
            account.Version += 1;
        }

        await new ApplicationEventOutbox(db).AddAsync(new ExchangeAccountUpdatedEventV1(
            Guid.NewGuid(), DateTimeOffset.UtcNow, userId, accountId));
        await transaction.CommitAsync();
    }
}
