using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
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
        var scenario = Environment.GetEnvironmentVariable("REALTIME_E2E_SCENARIO") ?? "account";
        if (scenario is not ("account" or "position")) throw new InvalidOperationException("Неизвестный сценарий.");
        if (scenario == "position")
        {
            await RunPosition(connection, userId, accountId);
            return;
        }
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
    private static Guid RequiredId(string name) =>
        Guid.TryParse(Environment.GetEnvironmentVariable(name), out var id) && id != Guid.Empty
            ? id : throw new InvalidOperationException($"Требуется корректный {name}.");

    private static PositionStateEventPayloadV1 Snapshot(PositionEntity p) => new(
        p.Size, p.AverageEntryPrice, p.PositionValue, p.Leverage, p.MarkPrice,
        p.BreakEvenPrice, p.LiquidationPrice, p.UnrealizedPnl, p.TakeProfit, p.StopLoss, p.TrailingStop);

    private static async Task RunPosition(string connection, Guid userId, Guid accountId)
    {
        var positionId = RequiredId("REALTIME_E2E_POSITION_ID");
        var phase = Environment.GetEnvironmentVariable("REALTIME_E2E_PHASE");
        if (phase is not ("initial" or "changed" or "recovered" or "wait-processed" or "cleanup"))
            throw new InvalidOperationException("Неизвестная фаза.");
        var eventId = phase == "cleanup" ? Guid.Empty : RequiredId("REALTIME_E2E_EVENT_ID");
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>().UseNpgsql(connection).Options;
        await using var db = new TradeSystemDbContext(options);
        if (phase == "wait-processed")
        {
            var ownerMatches = await db.ExchangeAccounts.AsNoTracking().AnyAsync(x => x.Id == accountId && x.UserId == userId
                && x.ProviderAccountId == "g07-test-" + accountId.ToString("N"));
            var positionMatches = await db.Positions.AsNoTracking().AnyAsync(x => x.Id == positionId && x.ExchangeAccountId == accountId);
            if (!ownerMatches || !positionMatches) throw new InvalidOperationException("Тестовая позиция не принадлежит пользователю.");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var message = await db.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == eventId);
                if (message is not null)
                {
                    var storedEvent = ApplicationEventSerializer.Deserialize(message.EventType, message.SchemaVersion, message.Payload);
                    var matches = storedEvent switch
                    {
                        PositionOpenedEventV1 opened => opened.UserId == userId && opened.ExchangeAccountId == accountId && opened.PositionId == positionId,
                        PositionChangedEventV1 changed => changed.UserId == userId && changed.ExchangeAccountId == accountId && changed.PositionId == positionId,
                        _ => false,
                    };
                    if (!matches) throw new InvalidOperationException("Outbox event не принадлежит тестовой позиции.");
                    if (message.ProcessedAt is not null) return;
                }
                await Task.Delay(250);
            }
            throw new TimeoutException("Outbox event не обработан.");
        }
        if (phase == "initial") await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var account = await db.ExchangeAccounts.SingleOrDefaultAsync(x => x.Id == accountId);
        var position = await db.Positions.SingleOrDefaultAsync(x => x.Id == positionId);
        var providerId = "g07-test-" + accountId.ToString("N");
        if (account is not null && (account.UserId != userId || account.ProviderAccountId != providerId))
            throw new InvalidOperationException("Тестовый аккаунт не принадлежит пользователю.");
        if (position is not null && (account is null || position.ExchangeAccountId != accountId))
            throw new InvalidOperationException("Позиция не принадлежит тестовому аккаунту.");
        if (phase == "cleanup")
        {
            if (account is null || position is null) throw new InvalidOperationException("Тестовая позиция или аккаунт не найдены.");
            db.PositionChanges.RemoveRange(await db.PositionChanges.Where(x => x.PositionId == positionId).ToListAsync());
            db.Positions.Remove(position);
            db.ExchangeAccounts.Remove(account);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var sequence = phase == "initial" ? 1 : phase == "changed" ? 2 : 3;
        if (phase == "initial")
        {
            if (position is not null) throw new InvalidOperationException("Позиция уже существует.");
            if (account is null)
            {
                account = new ExchangeAccountEntity { Id = accountId, UserId = userId, ExchangeId = ExchangeId.Bybit,
                    ProviderAccountId = providerId, DisplayName = "G07 position test", ConnectionStatus = ExchangeAccountConnectionStatus.Disabled,
                    Capabilities = ExchangeAccountCapabilities.None, Version = 1 };
                db.ExchangeAccounts.Add(account);
            }
            position = new PositionEntity { Id = positionId, ExchangeAccountId = accountId, InstrumentId = "BTCUSDT",
                MarketCategory = MarketCategory.Linear, PositionSide = PositionSide.Long, PositionIdx = 0, SettlementAsset = "USDT",
                FirstDetectedAt = now, TrackingState = PositionTrackingState.Active };
            db.Positions.Add(position);
        }
        else if (account is null || position is null || position.Version != sequence - 1 || position.Size != sequence - 1)
            throw new InvalidOperationException("Предыдущая фаза позиции не найдена.");
        var before = sequence == 1 ? null : Snapshot(position);
        position.Size = sequence;
        position.AverageEntryPrice = 60000m;
        position.MarkPrice = 60010m;
        position.Leverage = 5m;
        position.PositionValue = position.Size * position.MarkPrice;
        position.UnrealizedPnl = position.Size * 10m;
        position.Version = sequence;
        position.LastObservedAt = now;
        var after = Snapshot(position);
        var kind = sequence == 1 ? PositionChangeKind.New : PositionChangeKind.Increased;
        var cause = sequence == 1 ? PositionChangeCause.InitialObservation : PositionChangeCause.ExchangeObservation;
        db.PositionChanges.Add(new PositionChangeEntity { PositionId = positionId, Sequence = sequence, Kind = kind, Cause = cause,
            OccurredAt = now, TrackingStateAfter = position.TrackingState,
            BeforeSize = before?.Size, BeforeAverageEntryPrice = before?.AverageEntryPrice, BeforePositionValue = before?.PositionValue,
            BeforeLeverage = before?.Leverage, BeforeMarkPrice = before?.MarkPrice, BeforeBreakEvenPrice = before?.BreakEvenPrice,
            BeforeLiquidationPrice = before?.LiquidationPrice, BeforeUnrealizedPnl = before?.UnrealizedPnl,
            BeforeTakeProfit = before?.TakeProfit, BeforeStopLoss = before?.StopLoss, BeforeTrailingStop = before?.TrailingStop,
            AfterSize = after.Size, AfterAverageEntryPrice = after.AverageEntryPrice, AfterPositionValue = after.PositionValue,
            AfterLeverage = after.Leverage, AfterMarkPrice = after.MarkPrice, AfterBreakEvenPrice = after.BreakEvenPrice,
            AfterLiquidationPrice = after.LiquidationPrice, AfterUnrealizedPnl = after.UnrealizedPnl,
            AfterTakeProfit = after.TakeProfit, AfterStopLoss = after.StopLoss, AfterTrailingStop = after.TrailingStop });
        IApplicationEvent applicationEvent = sequence == 1
            ? new PositionOpenedEventV1(eventId, now, userId, accountId, ExchangeId.Bybit,
                positionId, sequence, position.InstrumentId, position.MarketCategory, position.PositionSide, position.PositionIdx,
                position.FirstDetectedAt, position.LastObservedAt, position.ClosedAt, kind, cause, position.TrackingState, before, after)
            : new PositionChangedEventV1(eventId, now, userId, accountId, ExchangeId.Bybit,
                positionId, sequence, position.InstrumentId, position.MarketCategory, position.PositionSide, position.PositionIdx,
                position.FirstDetectedAt, position.LastObservedAt, position.ClosedAt, kind, cause, position.TrackingState, before, after);
        await new ApplicationEventOutbox(db).AddAsync(applicationEvent);
        await transaction.CommitAsync();
    }

}
