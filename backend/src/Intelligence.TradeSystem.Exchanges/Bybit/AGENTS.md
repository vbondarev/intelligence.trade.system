# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.Exchanges/Bybit` и дополняет `../../AGENTS.md` только правилами Bybit adapter boundary.

## Назначение каталога

- `Public` реализует публичные market capabilities.
- `PrivateAccounts` реализует чтение данных конкретного пользовательского аккаунта.
- `Mapping` нормализует модели Bybit.Net в внутренние contracts.

Bybit-specific transport details должны оставаться внутри этого каталога.

## Границы адаптера

- Не протаскивай типы Bybit.Net в `Domain`, `Application`, `MarketIntelligence` или публичные API contracts.
- Возвращай нормализованные внутренние модели (`Ticker`, `OrderBook`, `Kline`, `FundingRateEntry` и т.п.), а не raw Bybit response types.
- Публичные capabilities не используют пользовательские credentials.
- Private provider/client создаётся для конкретных credentials; не сохраняй расшифрованные credentials в singleton/global state.
- Интеграция первого MVP остаётся read-only. Не добавляй создание/изменение/закрытие ордеров, изменение плеча, переводы средств или другие trading actions без отдельного этапа ROADMAP.

## Mapping и transport semantics

- Числовую, enum- и string-normalization выполняй на exchange boundary.
- Сохраняй UTC/`DateTimeOffset` semantics при преобразовании временных значений.
- Switch expressions для category/interval/account type должны быть исчерпывающими и fail-fast для неподдерживаемых значений.
- Derivatives-only операции не должны молча работать для Spot.
- Нулевые позиции не должны превращаться в активные Domain positions.
- Не меняй существующую null/empty/failure semantics транспорта без проверки всех Application consumers и тестов.

## Ошибки и устойчивость

- Не выпускай transport-specific exceptions как часть публичного application contract, если существующий adapter contract нормализует ошибку иначе.
- Сохраняй ограниченную политику retry/timeout существующего Bybit adapter; не наслаивай второй независимый resilience pipeline без отдельного решения.
- Caller cancellation должна передаваться корректно и не превращаться в автоматический retry.

## Логирование и телеметрия

- Используй существующие structured logging/telemetry helpers вместо произвольных строк там, где они уже определены.
- Не логируй API key, API secret, access token, decrypted credential material или raw headers.
- Metric tags должны оставаться низкокардинальными; не добавляй symbol, account ID, user ID или credentials как metric dimensions.
- Нормализуй failure kind вместо публикации raw exception/message как metric label.

## При изменении кода

Если меняется request parameter, mapping или transport behavior:

- обнови `Intelligence.TradeSystem.Exchanges.Tests`;
- проверь затронутые `Intelligence.TradeSystem.Application.Tests`;
- проследи влияние новых полей до `CollectedPublicMarketData`, `PublicMarketDataCollector`, `MarketSnapshotService` и downstream assemblers, если изменение относится к public market data;
- проверь private sync/reconciliation tests, если изменяется private account mapping.

Если DI surface адаптера меняется, обнови соответствующий `StartupExtensions` и проверь scope/lifetime: пользовательские credentials не должны становиться захваченным singleton-состоянием.
