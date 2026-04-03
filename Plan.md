# Intelligence Trade System — План реализации

> Последнее обновление: 2026-04-04
> Текущий статус: **Фаза 1 — завершение слоя Indicators**

---

## Текущее положение

```
Фаза 1 █████████░░░░░░░░░░░░░  Exchanges ✅ | Assemblers 8/12 ✅ | + 4 осталось
Фаза 2 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 3 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 4 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 5 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 6 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 7 ░░░░░░░░░░░░░░░░░░░░░░  тесты частично (TimeframeSnapshotAssemblerTests)
```

---

## Реализованные компоненты

### Bybit эндпоинты (`Intelligence.TradeSystem.Exchanges`)

| Статус | Эндпоинт | Метод `IBybitProvider` | Доменная модель |
|--------|----------|------------------------|-----------------|
| ✅ | `/v5/market/kline` | `GetKlinesAsync` | `IReadOnlyList<Kline>` |
| ✅ | `/v5/market/tickers` | `GetTickerAsync` | `Ticker?` (+ derivative fields) |
| ✅ | `/v5/market/orderbook` | `GetOrderBookAsync` | `OrderBook?` |
| ✅ | `/v5/market/recent-trade` | `GetRecentTradesAsync` | `IReadOnlyList<Trade>` |
| ✅ | `/v5/market/open-interest` | `GetOpenInterestHistoryAsync` | `IReadOnlyList<OpenInterestEntry>` |
| ✅ | `/v5/market/funding/history` | `GetFundingRateHistoryAsync` | `IReadOnlyList<FundingRateEntry>` |
| ✅ | `/v5/market/account-ratio` | `GetLongShortRatioHistoryAsync` | `IReadOnlyList<LongShortRatioEntry>` |
| ✅ | `/v5/account/wallet-balance` | `GetWalletBalanceAsync` | `AccountBalance?` |
| ✅ | `/v5/position/list` | `GetOpenPositionsAsync` | `IReadOnlyList<OpenPosition>` |
| ❌ | `/v5/market/instruments-info` | `GetInstrumentInfoAsync` | `InstrumentInfo?` |

### Ассемблеры (`Intelligence.TradeSystem.Indicators`)

| Статус | Ассемблер | Вход | Выход |
|--------|-----------|------|-------|
| ✅ | `PriceSnapshotAssembler` | `Ticker` | `PriceSnapshot` |
| ✅ | `TimeframeSnapshotAssembler` | `IReadOnlyList<Kline>` | `TimeframeAnalysisSnapshot` |
| ✅ | `OrderBookSnapshotAssembler` | `OrderBook` | `OrderBookSnapshot` |
| ✅ | `TradeFlowSnapshotAssembler` | `IReadOnlyList<Trade>` | `TradeFlowSnapshot` |
| ✅ | `OpenInterestSnapshotAssembler` | `IReadOnlyList<OpenInterestEntry>` | `OpenInterestSnapshot` |
| ✅ | `FundingRateSnapshotAssembler` | `IReadOnlyList<FundingRateEntry>` | `FundingRateSnapshot` |
| ✅ | `LongShortRatioSnapshotAssembler` | `IReadOnlyList<LongShortRatioEntry>` | `LongShortRatioSnapshot` |
| ✅ | `DerivativesSnapshotAssembler` | `Ticker` + `FundingRateSnapshot` + `OpenInterestSnapshot` + `LongShortRatioSnapshot` | `DerivativesSnapshot` |
| ❌ | `SentimentSnapshotAssembler` | `FundingRateSnapshot` + `LongShortRatioSnapshot` + `OrderBookSnapshot` + `TradeFlowSnapshot` | `SentimentSnapshot` |
| ❌ | `OpenPositionSnapshotAssembler` | `OpenPosition` | `OpenPositionSnapshot` |
| ❌ | `PortfolioSnapshotAssembler` | `AccountBalance` + `IReadOnlyList<OpenPosition>` | `PortfolioSnapshot` |
| ❌ | `MarketAnalysisSnapshotAssembler` | все снапшоты | `MarketAnalysisSnapshot` |

### Вспомогательные компоненты (`Indicators`)

| Статус | Компонент |
|--------|-----------|
| ✅ | `EmaCalculator` |
| ✅ | `RsiCalculator` |
| ✅ | `AtrCalculator` |
| ✅ | `SmaCalculator` |
| ✅ | `VolumeProfileDetector` |
| ✅ | `TrendClassifier` |

---

## Фаза 1 — Завершение слоя Indicators `[текущий этап]`

- [x] **1.1** `DerivativesSnapshotAssembler`
  - Вход: `Ticker` (текущий funding rate, open interest) + `FundingRateSnapshot` + `OpenInterestSnapshot` + `LongShortRatioSnapshot`
  - Выход: `DerivativesSnapshot`
  - Вычисляет: `PremiumVsIndexPct`, `FundingRateAvg24h`, `OpenInterestChange1hPct / 4hPct`, `LongRatio / ShortRatio`

- [ ] **1.2** `SentimentSnapshotAssembler`
  - Вход: `FundingRateSnapshot` + `LongShortRatioSnapshot` + `OrderBookSnapshot` + `TradeFlowSnapshot`
  - Выход: `SentimentSnapshot`
  - Все скоры нормализованы в `[-1, 1]`; определяет `MarketRegime` (Trending / MeanReversion / Volatile / Neutral)

- [ ] **1.3** `OpenPositionSnapshotAssembler`
  - Вход: `OpenPosition`
  - Выход: `OpenPositionSnapshot`
  - Вычисляет: `UnrealizedPnlPct = UnrealizedPnl / PositionValue × 100` (safe division)

- [ ] **1.4** `PortfolioSnapshotAssembler`
  - Вход: `AccountBalance` + `IReadOnlyList<OpenPosition>`
  - Выход: `PortfolioSnapshot`
  - Заполняет `TotalEquityUsd`, `AvailableBalanceUsd`, `TotalWalletBalanceUsd`, `TotalUnrealizedPnlUsd`, `OpenPositions`

- [ ] **1.5** `MarketAnalysisSnapshotAssembler`
  - Финальный оркестратор: принимает все готовые снапшоты, возвращает `MarketAnalysisSnapshot`

- [ ] **1.6** Тесты на новые ассемблеры в `Intelligence.TradeSystem.Indicators.Tests`

---

## Фаза 2 — Проект `Intelligence.TradeSystem.Application`

- [ ] **2.1** Интерфейс `IMarketDataCollector` — декларирует сбор всех рыночных данных по символу
- [ ] **2.2** `MarketDataCollector` — параллельно вызывает все `GetXxxAsync` через `IBybitProvider`
- [ ] **2.3** Интерфейс `IMarketAnalysisService` — `BuildSnapshotAsync(string symbol, MarketCategory category) → MarketAnalysisSnapshot`
- [ ] **2.4** `MarketAnalysisService` — вызывает `MarketDataCollector`, прогоняет через все ассемблеры

---

## Фаза 3 — Проект `Intelligence.TradeSystem.Analytics`

- [ ] **3.1** `IAnalyticsFormatter` — интерфейс форматирования снапшота в текст для GPT
- [ ] **3.2** `SnapshotTextFormatter` — секции: цена, деривативы, стакан, тренд, портфель
- [ ] **3.3** `IMarketRegimeClassifier` — определяет рыночный режим на основе мультифреймовых данных
- [ ] **3.4** `MarketRegimeClassifier` — реализация на основе `TrendStrengthScore` и `TradeFlowSnapshot`

---

## Фаза 4 — Проект `Intelligence.TradeSystem.Ai`

- [ ] **4.1** `IPromptBuilder` — строит GPT-prompt из `MarketAnalysisSnapshot` + запроса пользователя
- [ ] **4.2** `PromptBuilder` — шаблон системного промпта + форматированные данные из `Analytics`
- [ ] **4.3** `IGptAnalyticsService` — `AnalyzeAsync(MarketAnalysisSnapshot, string userQuery) → string`
- [ ] **4.4** `GptAnalyticsService` — интеграция с OpenAI SDK (`chat/completions`)

---

## Фаза 5 — Проект `Intelligence.TradeSystem.Api` (Telegram Bot)

- [ ] **5.1** Интеграция `Telegram.Bot` SDK, настройка webhook/polling
- [ ] **5.2** `TelegramUpdateHandler` — обрабатывает входящие сообщения, маршрутизирует команды
- [ ] **5.3** Команда `/analyze <symbol>` — вызывает `IMarketAnalysisService` + `IGptAnalyticsService`
- [ ] **5.4** Форматтер ответа для Telegram — разбивка на части, Markdown-разметка

---

## Фаза 6 — Инфраструктура и хост

- [ ] **6.1** `Intelligence.TradeSystem.Infrastructure` — конфигурация DI: Bybit client с API-ключами, OpenAI client
- [ ] **6.2** `Intelligence.TradeSystem.Persistence` — кэширование снапшотов (Redis / in-memory), история запросов
- [ ] **6.3** `Intelligence.TradeSystem.Worker` — фоновый сервис: периодическое обновление данных, инвалидация кэша
- [ ] **6.4** `Intelligence.TradeSystem.Backend.Host` — подключение реальных сервисов, `appsettings` конфигурация

---

## Фаза 7 — Тесты и качество

- [ ] **7.1** `UnitTests` — покрытие ассемблеров, форматтеров, классификаторов
- [ ] **7.2** `IntegrationTests` — `IBybitProvider` против Bybit testnet, end-to-end сборка `MarketAnalysisSnapshot`
- [ ] **7.3** `ArchitectureTests` — `NetArchTest`: проверка зависимостей между слоями
- [ ] **7.4** `/v5/market/instruments-info` — шаг цены, лот-сайз для нормализации отображения в Telegram

---

## Архитектура зависимостей

```
Domain          ← нет зависимостей (только BCL)
Abstractions    ← Domain
Exchanges       ← Abstractions, Domain, Bybit.Net SDK
Indicators      ← Domain
Application     ← Abstractions, Domain, Indicators
Analytics       ← Domain, Indicators
Ai              ← Domain, Analytics, OpenAI SDK
Infrastructure  ← Application, Exchanges, Ai, Persistence
Persistence     ← Domain
Worker          ← Infrastructure
Api             ← Application, Ai, Telegram.Bot SDK
Backend.Host    ← Infrastructure, Worker, Api
```

---

## Пользовательский сценарий (целевой)

```
Пользователь → Telegram → /analyze BTCUSDT
  → TelegramUpdateHandler
  → IMarketAnalysisService.BuildSnapshotAsync("BTCUSDT", Linear)
      → IBybitProvider × 9 endpoints (параллельно)
      → Assemblers × 12 (последовательно)
      → MarketAnalysisSnapshot
  → IGptAnalyticsService.AnalyzeAsync(snapshot, "внутридневная торговля")
      → PromptBuilder → prompt string
      → OpenAI chat/completions → аналитика
  → Telegram → пользователь получает ответ
```
