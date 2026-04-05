# Intelligence Trade System — План реализации

> Последнее обновление: 2026-04-05
> Текущий статус: **Фаза 1 завершена. Следующий шаг — тесты оставшихся Assembler'ов (1.5), затем Фаза 2**

---

## Текущее положение

```
Фаза 1 ████████████████████░░  Exchanges 9/10 ✅ | Assemblers 11/11 ✅ | Indicator тесты ✅ | Assembler тесты 1/5 🔄
Фаза 2 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 3 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 4 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 5 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 6 ░░░░░░░░░░░░░░░░░░░░░░  не начата
Фаза 7 ████████░░░░░░░░░░░░░░  Indicators.Tests ✅ 107 тестов | Analysis.Tests 🔄 14/~55 тестов
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
| ✅ | `PortfolioSnapshotAssembler` | `AccountBalance?` + `IReadOnlyList<OpenPosition>` | `PortfolioSnapshot` (включает маппинг `OpenPositionSnapshot`) |
| ✅ | `SentimentSnapshotAssembler` | `DerivativesSnapshot` + `OrderBookSnapshot` + `TradeFlowSnapshot` + `TimeframeAnalysisSnapshot` (H1) + `TimeframeAnalysisSnapshot` (H4) | `SentimentSnapshot` |
| ✅ | `MarketAnalysisSnapshotAssembler` | все снапшоты | `MarketAnalysisSnapshot` |

> **Примечание:** `OpenPositionSnapshotAssembler` как отдельный класс не создаётся —
> маппинг `OpenPosition → OpenPositionSnapshot` реализован как приватный метод `MapPosition`
> внутри `PortfolioSnapshotAssembler`.

### Вспомогательные компоненты (`Indicators`)

| Статус | Компонент |
|--------|-----------|
| ✅ | `EmaCalculator` |
| ✅ | `RsiCalculator` |
| ✅ | `AtrCalculator` |
| ✅ | `SmaCalculator` |
| ✅ | `VolumeProfileDetector` |
| ✅ | `TrendClassifier` |

### Тесты (`Intelligence.TradeSystem.Indicators.Tests`) — 107 тестов ✅

| Статус | Тест |
|--------|------|
| ✅ | `EmaCalculatorTests` |
| ✅ | `RsiCalculatorTests` |
| ✅ | `AtrCalculatorTests` |
| ✅ | `SmaCalculatorTests` |
| ✅ | `VolumeProfileDetectorTests` |
| ✅ | `TrendClassifierTests` |

### Тесты (`Intelligence.TradeSystem.Analysis.Tests`) — 14 тестов ✅

> ⚠️ Проект имел ошибку компиляции: `<Using Include="NUnit.Framework"/>` без NUnit-пакета — **исправлено 2026-04-05**.

| Статус | Тест |
|--------|------|
| ✅ | `TimeframeSnapshotAssemblerTests` |
| ❌ | `PriceSnapshotAssemblerTests` |
| ❌ | `TradeFlowSnapshotAssemblerTests` |
| ❌ | `OrderBookSnapshotAssemblerTests` |
| ❌ | `OpenInterestSnapshotAssemblerTests` |
| ❌ | `FundingRateSnapshotAssemblerTests` |
| ❌ | `LongShortRatioSnapshotAssemblerTests` |
| ❌ | `DerivativesSnapshotAssemblerTests` |
| ❌ | `PortfolioSnapshotAssemblerTests` |
| ❌ | `SentimentSnapshotAssemblerTests` |
| ❌ | `MarketAnalysisSnapshotAssemblerTests` |

---

## Фаза 1 — Завершение слоя Indicators `[текущий этап]`

- [x] **1.1** `DerivativesSnapshotAssembler`
  - Вход: `Ticker` (текущий funding rate, open interest) + `FundingRateSnapshot` + `OpenInterestSnapshot` + `LongShortRatioSnapshot`
  - Выход: `DerivativesSnapshot`
  - Вычисляет: `PremiumVsIndexPct`, `FundingRateAvg24h`, `OpenInterestChange1hPct / 4hPct`, `LongRatio / ShortRatio`

- [x] **1.2** `PortfolioSnapshotAssembler`
  - Вход: `AccountBalance?` + `IReadOnlyList<OpenPosition>`
  - Выход: `PortfolioSnapshot` с вложенными `OpenPositionSnapshot`
  - Маппинг позиций реализован приватным методом `MapPosition`; позиции с `Size = 0` пропускаются

- [x] **1.3** `SentimentSnapshotAssembler`
  - Вход: `DerivativesSnapshot` + `OrderBookSnapshot` + `TradeFlowSnapshot` + `TimeframeAnalysisSnapshot` (H1, H4)
  - Выход: `SentimentSnapshot`
  - Все скоры нормализованы в `[-1, 1]`; определяет `MarketRegime` (Trending / MeanReversion / Volatile / Neutral)

- [x] **1.4** `MarketAnalysisSnapshotAssembler`
  - Финальный оркестратор: принимает все готовые снапшоты, возвращает `MarketAnalysisSnapshot`
  - `Category` нормализуется в lowercase (`Linear` → `"linear"`)
  - `Tags` формируются автоматически из данных снапшотов (regime, funding, RSI, orderbook, tradeflow)

- [ ] **1.5** Тесты на оставшиеся ассемблеры в `Intelligence.TradeSystem.Analysis.Tests`
  - [ ] **1.5.1** `MarketAnalysisSnapshotAssemblerTests` — центральный оркестратор, наивысший приоритет
  - [ ] **1.5.2** `PriceSnapshotAssemblerTests`
  - [ ] **1.5.3** `TradeFlowSnapshotAssemblerTests`
  - [ ] **1.5.4** `DerivativesSnapshotAssemblerTests`
  - [ ] **1.5.5** `PortfolioSnapshotAssemblerTests`
  - [ ] **1.5.6** `SentimentSnapshotAssemblerTests`
  - [ ] **1.5.7** `OrderBookSnapshotAssemblerTests`, `OpenInterestSnapshotAssemblerTests`, `FundingRateSnapshotAssemblerTests`, `LongShortRatioSnapshotAssemblerTests`

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
      → Assemblers × 11 (последовательно)
      → MarketAnalysisSnapshot
  → IGptAnalyticsService.AnalyzeAsync(snapshot, "внутридневная торговля")
      → PromptBuilder → prompt string
      → OpenAI chat/completions → аналитика
  → Telegram → пользователь получает ответ
```





