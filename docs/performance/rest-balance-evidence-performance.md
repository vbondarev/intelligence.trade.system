# Производительность REST Balance evidence

Дата измерения: 2026-10-25T17:45:21.8515621+00:00
Образ PostgreSQL: `postgres:16-alpine`
Версия PostgreSQL: `PostgreSQL 16.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit`
Docker Engine: `29.7.2`
Среда теста: Microsoft Windows NT 10.0.26300.0; .NET 10.0.11; SDK 10.0.400; CPU threads 16.

Для каждого варианта и набора выполнен прогрев (1) и 3 измерения. Таблица содержит медианы; полные результаты, SQL, параметры и планы находятся в [JSON evidence](./rest-balance-evidence-performance-evidence.json).
Оценка числа строк PostgreSQL после `ANALYZE`:
- `exchange_accounts`: 5.
- `exchange_rest_ordering_intervals`: 64,600.
SQL duration измеряется до возврата `DbDataReader`; wall time включает полное чтение и EF materialization. `EXPLAIN` выполнялся отдельно и не входит в wall time.
После наполнения таблиц перед измерениями выполнен ANALYZE public.exchange_accounts и ANALYZE public.exchange_rest_ordering_intervals.

Основной сценарий сохранён для 100, 10 000 и 50 000 интервалов. Дополнительно на историях 10 000 и 50 000 интервалов измерены zero-match и объединение результатов для разных `observedAt` и `messageCreatedAt`. Baseline материализует всю историю и фильтрует её в памяти; Optimized переносит строгую временную фильтрацию в PostgreSQL. Для каждого сценария ID совпадают.

## Измерения

| Сценарий | История Balance | Вариант | SQL round trips | Materialized rows | SQL duration, ms | Метод, ms |
|---|---:|---|---:|---:|---:|---:|
| Основной | 100 | Baseline | 1 | 100 | 1.712 | 2.895 |
| Основной | 100 | Optimized | 1 | 3 | 1.828 | 3.026 |
| Основной | 10,000 | Baseline | 1 | 10000 | 2.229 | 25.105 |
| Основной | 10,000 | Optimized | 1 | 3 | 2.219 | 3.268 |
| Без совпадений | 10,000 | Baseline | 1 | 10000 | 2.717 | 30.084 |
| Без совпадений | 10,000 | Optimized | 1 | 0 | 2.605 | 3.717 |
| Разные observedAt/messageCreatedAt | 10,000 | Baseline | 1 | 10000 | 2.374 | 23.487 |
| Разные observedAt/messageCreatedAt | 10,000 | Optimized | 1 | 3 | 2.748 | 3.589 |
| Основной | 50,000 | Baseline | 1 | 50000 | 2.714 | 110.062 |
| Основной | 50,000 | Optimized | 1 | 3 | 6.335 | 7.289 |
| Без совпадений | 50,000 | Baseline | 1 | 50000 | 2.719 | 123.736 |
| Без совпадений | 50,000 | Optimized | 1 | 0 | 5.541 | 6.292 |
| Разные observedAt/messageCreatedAt | 50,000 | Baseline | 1 | 50000 | 2.307 | 73.044 |
| Разные observedAt/messageCreatedAt | 50,000 | Optimized | 1 | 3 | 7.282 | 7.991 |

## Наборы данных

| История Balance | Другой аккаунт того же user | Другой user | Position evidence на account с историей 10,000 | Кандидаты |
|---:|---:|---:|---:|---:|
| 100 | 2,000 | 2,000 | 500 | 3 |
| 10,000 | 2,000 | 2,000 | 500 | 3 |
| 50,000 | 2,000 | 2,000 | 500 | 3 |

## Семейство и индексы

Все запросы и полные `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` планы с параметрами сохранены в JSON evidence. Индексы, использованные в этих планах: `ix_exchange_rest_ordering_intervals_account_kind_bounds`.

| Сценарий | История Balance | Вариант | Оценка строк | Фактические строки | Удалено фильтром | Shared buffer blocks (hit/read) | EXPLAIN, ms | Использованные индексы |
|---|---:|---|---:|---:|---:|---:|---:|---|
| Основной | 100 | Baseline | 34 | 100 | 0 | 54/0 | 0.192 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Основной | 100 | Optimized | 1 | 3 | 0 | 7/0 | 0.130 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Основной | 10,000 | Baseline | 3,450 | 10,000 | 0 | 255/0 | 2.670 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Основной | 10,000 | Optimized | 27 | 3 | 0 | 97/0 | 0.744 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Основной | 50,000 | Baseline | 16,599 | 50,000 | 14,600 | 735/0 | 11.289 | нет |
| Основной | 50,000 | Optimized | 129 | 3 | 64,597 | 735/0 | 5.316 | нет |
| Без совпадений | 10,000 | Baseline | 3,450 | 10,000 | 0 | 255/0 | 2.771 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Без совпадений | 10,000 | Optimized | 1 | 0 | 0 | 94/0 | 0.698 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Разные observedAt/messageCreatedAt | 10,000 | Baseline | 3,450 | 10,000 | 0 | 255/0 | 2.740 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Разные observedAt/messageCreatedAt | 10,000 | Optimized | 53 | 3 | 0 | 191/0 | 1.273 | `ix_exchange_rest_ordering_intervals_account_kind_bounds` |
| Без совпадений | 50,000 | Baseline | 16,599 | 50,000 | 14,600 | 735/0 | 14.608 | нет |
| Без совпадений | 50,000 | Optimized | 2 | 0 | 64,600 | 735/0 | 7.078 | нет |
| Разные observedAt/messageCreatedAt | 50,000 | Baseline | 16,599 | 50,000 | 14,600 | 735/0 | 14.255 | нет |
| Разные observedAt/messageCreatedAt | 50,000 | Optimized | 257 | 3 | 64,597 | 735/0 | 9.521 | нет |

Для истории 50,000 PostgreSQL выбрал `Seq Scan` для optimized scan: оценка 129, факт 3, shared hit/read 735/0; baseline оценка/факт строк 16,599/50,000. Медианное время метода: baseline 110.062 ms, optimized 7.289 ms.
При 50,000 существующий индекс не был выбран планировщиком; этот результат зафиксирован, а не скрыт. В пределах измеренных наборов оптимизация уменьшила материализацию и полное время метода, поэтому эти измерения сами по себе не подтверждают необходимость нового индекса или изменения схемы.
После `ANALYZE` PostgreSQL оценил полный объём `exchange_rest_ordering_intervals` в 64,600 строк; при этом для baseline при истории 50,000 оценка строк сканирования по аккаунту осталась 16,599 при фактических 50,000. Эта остаточная погрешность планировщика отражена в evidence и не маскируется статистикой уровня таблицы.

Достаточность существующего индекса оценивается по фактическим планам, оценке/факту строк, buffer reads и замерам, а не только по наличию индекса. Новые индексы и миграции не добавлялись.

SQL round trips на вызов не увеличились: один для обоих вариантов. В основном сценарии и сценарии с разными timestamps optimized материализует три строки, в сценарии без совпадений — ноль; baseline материализует всю историю соответствующего аккаунта.

## Воспроизведение

Из `backend/src` в PowerShell; требуются Docker и PostgreSQL/Testcontainers:

```powershell
dotnet build .\Intelligence.TradeSystem.Infrastructure.IntegrationTests\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release
# Опционально, если Docker CLI недоступен: укажите фактическую версию Docker Engine.
# $env:ITS_DOCKER_ENGINE_VERSION = '<версия Docker Engine>'
# Если переменная не задана, версия читается через docker info.
$env:ITS_RUN_REST_BALANCE_EVIDENCE_PERFORMANCE = '1'
dotnet test .\Intelligence.TradeSystem.Infrastructure.IntegrationTests\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~RestBalanceEvidencePerformancePostgreSqlTests" --logger "console;verbosity=minimal"
```
