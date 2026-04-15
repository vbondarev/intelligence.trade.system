# Intelligence Trade System — План реализации

> Последнее обновление: 2026-04-15
> Текущий статус: **Фазы 1, 1.5, 2, 3, 4 и 5 завершены. В `Intelligence.TradeSystem.Api` реализованы controller-based route surface, endpoint `POST /api/analysis/snapshot`, endpoint `POST /api/analysis/ai`, DTO-контракты, базовые `/` и `/health` endpoints, валидация входных параметров, dev-only Swagger UI по `/swagger` и расширенный API regression/smoke test suite. В `Intelligence.TradeSystem.Ai` реализованы prompt-building contract, `PromptBuilder`, `ILlmAnalyticsService`, `LlmAnalyticsService`, `IOpenRouterClient`, `OpenRouterClient` и `LlmOptions`. `MarketAnalysisSnapshot` зафиксирован как основной payload для LLM. Пользовательский канал изменён на web-first (`Api` + `Web`). В фазе 6 начат Aspire bootstrap: добавлены `Intelligence.TradeSystem.ServiceDefaults` и `Intelligence.TradeSystem.AppHost`, проекты помещены в solution folder `Aspire`, `Intelligence.TradeSystem.Backend.Host` удалён из solution и workspace, а для ресурса `api` опубликована Swagger-ссылка через интерфейс Aspire. Секреты пока остаются в `appsettings.json`. Параллельный технический долг — `/v5/market/instruments-info`**

---

## Текущее положение

```
Фаза 1 ██████████████████████  Exchanges 9/10 ✅ | Assemblers 11/11 ✅ | Indicator тесты ✅ | Analysis assembler tests 11/11 ✅
Фаза 2 ██████████████████████  Application orchestration ✅ | DI registration ✅ | Application tests ✅
Фаза 3 ██████████████████████  formatter ✅ | shared regime policy ✅ | regime classifier ✅ | output composer ✅ | DI registration ✅ | Analytics tests ✅
Фаза 4 ██████████████████████  prompt contract ✅ | prompt builder ✅ | llm service contract ✅ | llm orchestration ✅ | llm options ✅ | provider integration ✅
Фаза 5 ██████████████████████  API scaffold ✅ | route surface ✅ | snapshot endpoint ✅ | ai endpoint ✅ | dto/health/validation ✅
Фаза 6 ████░░░░░░░░░░░░░░░░░░  Web UI / Infrastructure / Aspire bootstrap ~
Фаза 7 ███████████████░░░░░░░  Tests ✅ 437 тестов | Integration/Architecture ░░
```

---

## Реализованные компоненты

### Базовые абстракции (`Intelligence.TradeSystem.Abstractions`)

| Статус | Компонент | Назначение |
|--------|-----------|------------|
| ✅ | `ExchangeId` | Идентификатор поддерживаемой биржи в orchestration-слое |
| ✅ | `IMarketDataProvider` | Нейтральный контракт публичных market-data capability |
| ✅ | `IDerivativesDataProvider` | Нейтральный контракт производных/деривативных данных |
| ✅ | `IPrivateAccountProvider` | Нейтральный контракт приватных аккаунтных данных |
| ✅ | `IBybitProvider` | Временный compatibility-контракт поверх capability-интерфейсов для мягкой миграции |

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

> **Примечание:** текущая реализация биржевого слоя уже ориентирована на capability-интерфейсы
> (`IMarketDataProvider`, `IDerivativesDataProvider`, `IPrivateAccountProvider`).
> `IBybitProvider` сохранён как compatibility-обёртка и не является целевой долгосрочной абстракцией.

### Ассемблеры (`Intelligence.TradeSystem.Analysis`)

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

> **Примечание:** `MarketAnalysisSnapshot` является каноническим структурированным payload для downstream-слоёв.
> В LLM по умолчанию передаётся именно он (при необходимости вместе с компактным текстовым контекстом из `Analytics`),
> а не полный raw dump свечей/сделок/стакана.

### Вспомогательные компоненты (`Indicators`)

| Статус | Компонент |
|--------|-----------|
| ✅ | `EmaCalculator` |
| ✅ | `RsiCalculator` |
| ✅ | `AtrCalculator` |
| ✅ | `SmaCalculator` |
| ✅ | `VolumeProfileDetector` |
| ✅ | `TrendClassifier` |

### Тесты (`Intelligence.TradeSystem.Indicators.Tests`) — 109 тестов ✅

| Статус | Тест |
|--------|------|
| ✅ | `EmaCalculatorTests` |
| ✅ | `RsiCalculatorTests` |
| ✅ | `AtrCalculatorTests` |
| ✅ | `SmaCalculatorTests` |
| ✅ | `VolumeProfileDetectorTests` |
| ✅ | `TrendClassifierTests` |

### Тесты (`Intelligence.TradeSystem.Analysis.Tests`) — 163 теста ✅

| Статус | Тест |
|--------|------|
| ✅ | `TimeframeSnapshotAssemblerTests` |
| ✅ | `PriceSnapshotAssemblerTests` |
| ✅ | `TradeFlowSnapshotAssemblerTests` |
| ✅ | `OrderBookSnapshotAssemblerTests` |
| ✅ | `OpenInterestSnapshotAssemblerTests` |
| ✅ | `FundingRateSnapshotAssemblerTests` |
| ✅ | `LongShortRatioSnapshotAssemblerTests` |
| ✅ | `DerivativesSnapshotAssemblerTests` |
| ✅ | `PortfolioSnapshotAssemblerTests` |
| ✅ | `SentimentSnapshotAssemblerTests` |
| ✅ | `MarketAnalysisSnapshotAssemblerTests` |

### Тесты (`Intelligence.TradeSystem.Ai.Tests`) — 84 теста ✅

| Статус | Тест |
|--------|------|
| ✅ | `OpenRouterClientTests` |
| ✅ | `LlmOptionsTests` |
| ✅ | `LlmAnalyticsServiceTests` |
| ✅ | `ILlmAnalyticsServiceContractTests` |
| ✅ | `PromptBuilderTests` |
| ✅ | `PromptBuildRequestTests` |
| ✅ | `PromptBuildResultTests` |
| ✅ | `PromptMessageTests` |

### Тесты (`Intelligence.TradeSystem.Api.Tests`) — 28 тестов ✅

| Статус | Тест |
|--------|------|
| ✅ | `AnalysisRouteSurfaceTests` |
| ✅ | `SnapshotEndpointTests` |
| ✅ | `AiEndpointTests` |
| ✅ | `HealthEndpointTests` |
| ✅ | `CompositionRootSmokeTests` |
| ✅ | `RootEndpointTests` |
| ✅ | `SwaggerEndpointTests` |

### Тесты (`Intelligence.TradeSystem.Analytics.Tests`) — 32 теста ✅

| Статус | Тест |
|--------|------|
| ✅ | `AnalyticsOutputComposerTests` |
| ✅ | `MarketRegimeClassifierTests` |
| ✅ | `SnapshotTextFormatterTests` |
| ✅ | `StartupExtensionsTests` |

### Тесты (`Intelligence.TradeSystem.Application.Tests`) — 16 тестов ✅

| Статус | Тест |
|--------|------|
| ✅ | `MarketDataCollectorTests` |
| ✅ | `MarketAnalysisServiceTests` |
| ✅ | `StartupExtensionsTests` |

### Тесты (`Intelligence.TradeSystem.Exchanges.Tests`) — 5 тестов ✅

| Статус | Тест |
|--------|------|
| ✅ | `StartupExtensionsTests` |

> **Итого по solution:** `437` тестов, все проходят успешно.

---

## Фаза 1 — Завершение слоя Analysis `[завершено]`

- [x] **1.1** `DerivativesSnapshotAssembler`
  - Вход: `Ticker` (текущий funding rate, open interest) + `FundingRateSnapshot` + `OpenInterestSnapshot` + `LongShortRatioSnapshot`
  - Выход: `DerivativesSnapshot`
  - Вычисляет: `PremiumVsIndexPct`, `FundingRateAvg24h`, `OpenInterestChange1hPct / 4hPct`, `LongRatio / ShortRatio`

- [x] **1.2** `PortfolioSnapshotAssembler`
  - Вход: `AccountBalance?` + `IReadOnlyList<OpenPosition>`
  - Выход: `PortfolioSnapshot` с вложенными `OpenPositionSnapshot`
  - Маппинг позиций реализован приватным методом `MapPosition`; позиции с `Size <= 0` пропускаются

- [x] **1.3** `SentimentSnapshotAssembler`
  - Вход: `DerivativesSnapshot` + `OrderBookSnapshot` + `TradeFlowSnapshot` + `TimeframeAnalysisSnapshot` (H1, H4)
  - Выход: `SentimentSnapshot`
  - Все скоры нормализованы в `[-1, 1]`; определяет `MarketRegime` (Trending / MeanReversion / Volatile / Neutral)

- [x] **1.4** `MarketAnalysisSnapshotAssembler`
  - Финальный оркестратор: принимает все готовые снапшоты, возвращает `MarketAnalysisSnapshot`
  - `Category` нормализуется в lowercase (`Linear` → `"linear"`)
  - `Tags` формируются автоматически из данных снапшотов (regime, funding, RSI, orderbook, tradeflow)

- [x] **1.5** Тесты на ассемблеры в `Intelligence.TradeSystem.Analysis.Tests`
  - [x] **1.5.1** `MarketAnalysisSnapshotAssemblerTests` — центральный оркестратор, покрыт production-ready regression test suite
  - [x] **1.5.2** `PriceSnapshotAssemblerTests`
  - [x] **1.5.3** `TradeFlowSnapshotAssemblerTests`
  - [x] **1.5.4** `DerivativesSnapshotAssemblerTests`
  - [x] **1.5.5** `PortfolioSnapshotAssemblerTests`
  - [x] **1.5.6** `SentimentSnapshotAssemblerTests`
  - [x] **1.5.7** `OrderBookSnapshotAssemblerTests`, `OpenInterestSnapshotAssemblerTests`, `FundingRateSnapshotAssemblerTests`, `LongShortRatioSnapshotAssemblerTests`

---

## Фаза 2 — Проект `Intelligence.TradeSystem.Application` `[завершено]`

- [x] **2.1** Интерфейс `IMarketDataCollector` — декларирует сбор всех рыночных данных по символу и бирже
- [x] **2.2** `MarketDataCollector` — параллельно вызывает `GetXxxAsync` через нейтральные capability-интерфейсы
  - Зависит от `IMarketDataProvider`, `IDerivativesDataProvider`, `IPrivateAccountProvider`
  - Поддерживает `ExchangeId`, нормализацию `symbol`, `MarketCategory.Spot`/derivatives branching
- [x] **2.3** Интерфейс `IMarketAnalysisService` — `BuildSnapshotAsync(ExchangeId exchangeId, string symbol, MarketCategory category) → MarketAnalysisSnapshot`
- [x] **2.4** `MarketAnalysisService` — вызывает `MarketDataCollector`, валидирует критичные данные и прогоняет их через все ассемблеры
- [x] **2.5** `CollectedMarketData` — нормализованный пакет сырых данных для downstream-оркестрации
- [x] **2.6** `StartupExtensions.AddApplication()` — DI-регистрация orchestration-сервисов
- [x] **2.7** `Intelligence.TradeSystem.Application.Tests` — production-ready unit tests для collector/service/DI

---

## Фаза 3 — Проект `Intelligence.TradeSystem.Analytics`

- [x] **3.1** `Intelligence.TradeSystem.Analytics` — проект создан, базовые контракты `IAnalyticsFormatter` и `IMarketRegimeClassifier` добавлены
- [x] **3.2** `IAnalyticsFormatter` + `SnapshotTextFormatter` — реализован compact deterministic formatter по секциям: цена, деривативы, стакан, trade flow, тренд, сентимент, портфель
- [x] **3.3** `IMarketRegimeClassifier` — контракт зафиксирован поверх `MarketAnalysisSnapshot`, возвращает канонические значения `MarketRegimes`
- [x] **3.4** `MarketRegimeClassifier` — реализован с parity-логикой относительно текущей эвристики `SentimentSnapshotAssembler`
- [x] **3.5** `MarketRegimePolicy` — общий источник истины для эвристики `MarketRegime`, разделяемый `Analysis` и `Analytics`
- [x] **3.6** `AnalyticsOutput` + `IAnalyticsOutputComposer` + `AnalyticsOutputComposer` — единый downstream-friendly output contract, объединяющий `MarketRegime` и `FormattedContext`
- [x] **3.7** `StartupExtensions.AddAnalytics()` — DI-регистрация formatter / classifier / output composer + контрактные unit tests на resolution и scoped lifetime
- [x] **3.8** `SnapshotTextFormatterTests` — production-ready tests на null-guard, секции, placeholders, invariant culture и deterministic output
- [x] **3.9** XML-документация `Analytics` — синхронизирована с фактическим контрактом: слой работает поверх готового `MarketAnalysisSnapshot`, не пересчитывает raw exchange data и не формирует финальный user-facing ответ

> **Роль фазы:** не пересчитывать raw market data заново, а интерпретировать уже готовый
> `MarketAnalysisSnapshot` и при необходимости готовить компактный narrative-контекст для AI и UI.

---

## Фаза 4 — Проект `Intelligence.TradeSystem.Ai`

- [x] **4.1** `IPromptBuilder` + `PromptBuildRequest` + `PromptBuildResult` + `PromptMessage` + `PromptRole` — provider-neutral prompt-building contract поверх `MarketAnalysisSnapshot` + `AnalyticsOutput` + `userQuery`
- [x] **4.2** `PromptBuilder` — шаблон системного промпта + JSON-представление `MarketAnalysisSnapshot` + форматированные данные из `Analytics`
- [x] **4.3** `ILlmAnalyticsService` — `AnalyzeAsync(MarketAnalysisSnapshot snapshot, string userQuery) → string`
- [x] **4.4** `LlmAnalyticsService` — orchestration-сервис поверх `IAnalyticsOutputComposer`, `IPromptBuilder` и LLM provider client
- [x] **4.5** `IOpenRouterClient` / `OpenRouterClient` — реализована concrete интеграция с OpenRouter API (`chat/completions`) поверх `HttpClient`, `PromptBuildResult` и `LlmOptions`
- [x] **4.6** `LlmOptions` — конфигурация provider/baseUrl/apiKey/model/temperature/maxTokens

> **Роль фазы:** не вычислять индикаторы и уровни внутри LLM, а использовать уже подготовленный
> `MarketAnalysisSnapshot` как основной AI payload и получать интерпретацию / ответ на пользовательский вопрос через OpenRouter.

---

## Фаза 5 — Проект `Intelligence.TradeSystem.Api` (Web API)

- [x] **5.1** ASP.NET Core Web API — базовый composition root для HTTP endpoints
- [x] **5.2** `AnalysisController` / minimal endpoints — маршрутизация HTTP-запросов на snapshot- и AI-анализ
- [x] **5.3** Endpoint `POST /api/analysis/snapshot` — вызывает `IMarketAnalysisService` и возвращает `MarketAnalysisSnapshot`
- [x] **5.4** Endpoint `POST /api/analysis/ai` — вызывает `IMarketAnalysisService` + `ILlmAnalyticsService` и возвращает AI-аналитику
- [x] **5.5** DTO-модели запросов/ответов, базовый health endpoint, валидация входных параметров
- [x] **5.6** Swagger / OpenAPI для локальной разработки — классический `/swagger` включён только в `Development` и покрыт regression-тестами

---

## Фаза 6 — Web UI, инфраструктура и Aspire

- [ ] **6.1** `Intelligence.TradeSystem.Web` — web-клиент поверх `Intelligence.TradeSystem.Api` для ввода символа/категории/запроса и отображения результата
- [ ] **6.2** `Intelligence.TradeSystem.Infrastructure` — конфигурация внешних клиентов и окружения: `Bybit`, `OpenRouter`; на текущем этапе секреты хранятся в `appsettings.json`
- [ ] **6.3** `Intelligence.TradeSystem.Persistence` — кэширование снапшотов (Redis / in-memory), история запросов
- [x] **6.4** `Intelligence.TradeSystem.ServiceDefaults` — общие defaults для локального запуска и сервисной конфигурации
- [~] **6.5** `Intelligence.TradeSystem.AppHost` — Aspire AppHost для локального запуска и отладки `Intelligence.TradeSystem.Api`
  - [x] Добавить Aspire-проекты в solution folder `Aspire`
  - [x] Поднять через AppHost `Intelligence.TradeSystem.Api`
  - [x] Опубликовать ссылку `Swagger` для ресурса `api` в Aspire Dashboard
  - [ ] Подготовить расширение orchestration для будущих сервисов
- [x] **6.6** Удалить `Intelligence.TradeSystem.Backend.Host`
- [ ] **6.7** `Intelligence.TradeSystem.Worker` — фоновый сервис: периодическое обновление данных, инвалидация кэша

---

## Фаза 7 — Тесты и качество

- [x] **7.1** `Tests` — покрытие `Ai`, `Analytics`, `Indicators`, `Analysis`, `Application`, `Exchanges`, `Api`
  - `Intelligence.TradeSystem.Ai.Tests` — 84 теста
  - `Intelligence.TradeSystem.Analytics.Tests` — 32 теста
  - `Intelligence.TradeSystem.Indicators.Tests` — 109 тестов
  - `Intelligence.TradeSystem.Analysis.Tests` — 163 теста
  - `Intelligence.TradeSystem.Application.Tests` — 16 тестов
  - `Intelligence.TradeSystem.Exchanges.Tests` — 5 тестов
  - `Intelligence.TradeSystem.Api.Tests` — 28 тестов
- [ ] **7.2** `IntegrationTests` — `IBybitProvider` против Bybit testnet, end-to-end сборка `MarketAnalysisSnapshot`
- [ ] **7.3** `ArchitectureTests` — `NetArchTest`: проверка зависимостей между слоями
- [ ] **7.4** `/v5/market/instruments-info` — шаг цены, лот-сайз для нормализации отображения в Web UI / API ответах

---

## Архитектура зависимостей

```
Domain          ← нет зависимостей (только BCL)
Abstractions    ← Domain
Exchanges       ← Abstractions, Domain, Bybit.Net SDK
Indicators      ← Domain
Analysis        ← Domain, Indicators
Application     ← Abstractions, Domain, Analysis

Analytics       ← Domain, Indicators
Ai              ← Domain, Analytics, OpenRouter client integration
Persistence     ← planned (Domain)
Infrastructure  ← planned (Application, Exchanges, Ai, Persistence)
ServiceDefaults ← общие Aspire/service defaults
Api             ← Application, Exchanges, Analytics, Ai, ASP.NET Core, ServiceDefaults
Web             ← planned (HTTP client to Api)
Worker          ← planned (Infrastructure, ServiceDefaults)
AppHost         ← Aspire orchestration для Api и будущих сервисов
```

---

## Пользовательский сценарий (целевой)

```
Пользователь → Web UI → HTTP POST /api/analysis/ai
  → Analysis API endpoint
  → IMarketAnalysisService.BuildSnapshotAsync(Bybit, "BTCUSDT", Linear)
      → IMarketDataProvider / IDerivativesDataProvider / IPrivateAccountProvider × 9 endpoints (параллельно)
      → Assemblers × 11 (последовательно)
      → MarketAnalysisSnapshot
  → ILlmAnalyticsService.AnalyzeAsync(snapshot, "внутридневная торговля")
      → PromptBuilder → snapshot JSON + analytics context + user query
      → OpenRouter chat/completions → аналитика
  → Web API response → Web UI → пользователь получает ответ
```

---

## Ближайшие шаги

1. Подготовить расширение orchestration для будущих `Web` / `Persistence` / `Worker` поверх уже добавленного `Aspire AppHost` и resource links.
2. Проверить end-to-end локальный сценарий `AppHost → Api → Swagger` через интерфейс Aspire Dashboard.
3. На текущем этапе оставить секреты в `appsettings.json`.
4. Затем запустить **Фазу 6.1**: реализовать Web UI поверх API.
5. Закрыть технический долг по `/v5/market/instruments-info` для будущей нормализации ценовых шагов и лот-сайза.
6. Подготовить **integration tests** для exchange-слоя и end-to-end сборки `MarketAnalysisSnapshot`.





