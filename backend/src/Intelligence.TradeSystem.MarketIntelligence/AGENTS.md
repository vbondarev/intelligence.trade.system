# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.MarketIntelligence` и дополняет `../AGENTS.md` только правилами детерминированной рыночной аналитики.

Подробные контракты индикаторов, fallback/unavailable semantics и граничные случаи находятся в `INDICATOR_CONTRACTS.md`. Не дублируй их здесь.

## Назначение проекта

`MarketIntelligence` содержит:

- индикаторы;
- детерминированные assemblers и market analysis;
- timeframe evaluation;
- market regime classification;
- diagnostics;
- публичные market snapshot contracts.

Проект не обращается к бирже, HTTP, БД, filesystem и другим внешним источникам.

## Детерминизм и чистота

- Расчёты должны быть детерминированными и без побочных эффектов.
- Не используй текущую системную дату/время внутри расчётной логики; время должно приходить во входных данных.
- Не добавляй logging в indicators/чистые calculators.
- Не выполняй network/file/database IO.
- Не мутируй входные коллекции.
- Не добавляй скрытую зависимость от глобального состояния или DI в чистые вычисления.

## Входные данные

Рыночные временные ряды рассматриваются в хронологическом порядке `oldest -> newest`, если конкретный контракт явно не говорит иное.

`TimeframeSnapshotAssembler` сортирует klines по `StartTime` перед расчётами. Не ломай это предположение при изменении pipeline.

## Отсутствующие и fallback-значения

- Не используй `0m` как универсальную замену отсутствующему индикатору или уровню.
- Для scalar indicators сохраняй `IndicatorValue` semantics: available / fallback / unavailable.
- Во внешние snapshot/API/LLM contracts передавай отсутствующие scalar values как nullable значения и сопровождай диагностиками там, где это предусмотрено контрактом.
- Не сериализуй `IndicatorValue` напрямую во внешний wire contract.
- Изменение fallback, seed-window или unavailable semantics требует обновления `INDICATOR_CONTRACTS.md` и связанных тестов.

## Архитектурные границы

- `MarketIntelligence` может зависеть от Domain contracts, но не от `Api`, `Application`, `Infrastructure` или конкретного exchange SDK.
- `MarketSnapshot` содержит только публичные рыночные данные и не должен включать `PortfolioState`, позиции, пользователя, exchange account или credentials.
- API не должен дублировать вычисления MarketIntelligence; при изменении результата проверь downstream mapping, но оставляй расчёт в этом проекте.
- `MarketRegimePolicy` остаётся единым источником классификации market regime, пока архитектурное решение явно не изменено.

## Изменение алгоритмов

Если меняешь формулу, threshold, ordering, fallback или diagnostic behavior:

- обнови соответствующие `Intelligence.TradeSystem.MarketIntelligence.Tests`;
- проверь assemblers и snapshot contracts;
- проверь downstream API mapping/tests, если изменяется наблюдаемое значение;
- явно обнови контрактную документацию, если меняется публично зафиксированная semantics.

Не копируй числовые пороги и формулы в `AGENTS.md`: их источником истины должны быть код, тесты и специализированная контрактная документация.

## Тестирование

Тесты MarketIntelligence должны быть воспроизводимыми и проверять граничные значения, порядок входов, fallback/unavailable cases и отсутствие случайной зависимости от времени.

Для indicator fixtures переиспользуй существующие test helpers, если они подходят сценарию, вместо создания несовместимых ad-hoc наборов данных.
