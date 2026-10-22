# Производительность REST Balance evidence

Дата измерения: 2026-10-22T19:12:57.6700852+00:00
Образ PostgreSQL: `postgres:16-alpine`
Версия PostgreSQL: `PostgreSQL 16.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit`
Docker Engine: `29.7.2`
Среда теста: Microsoft Windows NT 10.0.26300.0; .NET 10.0.11; SDK 10.0.400; CPU threads 16.

Для каждого варианта и набора выполнен прогрев (1) и 3 измерения. Таблица содержит медианы; полные результаты, SQL, параметры и планы находятся в [JSON evidence](./rest-balance-evidence-performance-evidence.json).
SQL duration измеряется до возврата `DbDataReader`; wall time включает полное чтение и EF materialization. `EXPLAIN` выполнялся отдельно и не входит в wall time.

Baseline материализует всю историю Balance-интервалов аккаунта и фильтрует её в памяти. Optimized переносит строгую временную фильтрацию в PostgreSQL. Оба варианта возвращают одинаковые ID.

## Измерения

| История Balance | Вариант | SQL round trips | Materialized rows | SQL duration, ms | Метод, ms |
|---:|---|---:|---:|---:|---:|
| 100 | Baseline | 1 | 100 | 2.299 | 3.662 |
| 100 | Optimized | 1 | 3 | 1.846 | 2.933 |
| 10,000 | Baseline | 1 | 10000 | 1.519 | 39.198 |
| 10,000 | Optimized | 1 | 3 | 1.887 | 2.691 |
| 50,000 | Baseline | 1 | 50000 | 1.633 | 129.193 |
| 50,000 | Optimized | 1 | 3 | 5.076 | 6.222 |

## Наборы данных

| История Balance | Другой аккаунт того же user | Другой user | Position evidence на account с историей 10,000 | Кандидаты |
|---:|---:|---:|---:|---:|
| 100 | 2,000 | 2,000 | 500 | 3 |
| 10,000 | 2,000 | 2,000 | 500 | 3 |
| 50,000 | 2,000 | 2,000 | 500 | 3 |

## Семейство и индексы

Все запросы и полные `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` планы с параметрами сохранены в JSON evidence. Индексы, использованные в этих планах: `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id`.

| История Balance | Вариант | Строк в scan intervals | Удалено фильтром | EXPLAIN, ms | Использованные индексы |
|---:|---|---:|---:|---:|---|
| 100 | Baseline | 100 | 0 | 0.211 | `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id` |
| 100 | Optimized | 3 | 0 | 0.122 | `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id` |
| 10,000 | Baseline | 10,000 | 0 | 9.320 | `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id` |
| 10,000 | Optimized | 3 | 0 | 0.653 | `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id` |
| 50,000 | Baseline | 50,000 | 0 | 41.861 | `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id` |
| 50,000 | Optimized | 3 | 0 | 3.113 | `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ux_exchange_accounts_user_exchange_provider_account_id` |

Оптимизированный план считывает только три совпадения при каждом размере истории. Индексное решение основано на этих планах и измерениях; новый индекс или миграция не потребовались.

SQL round trips на вызов не увеличились: один для обоих вариантов. Число строк, материализованных optimized-запросом, равно трём совпадающим интервалам для каждого набора и не растёт вместе с историей.

## Воспроизведение

Из `backend/src` в PowerShell; требуются Docker и PostgreSQL/Testcontainers:

```powershell
dotnet build .\Intelligence.TradeSystem.Infrastructure.IntegrationTests\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release
$env:ITS_DOCKER_ENGINE_VERSION = docker info --format '{{.ServerVersion}}'
$env:ITS_RUN_REST_BALANCE_EVIDENCE_PERFORMANCE = '1'
dotnet test .\Intelligence.TradeSystem.Infrastructure.IntegrationTests\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~RestBalanceEvidencePerformancePostgreSqlTests" --logger "console;verbosity=minimal"
```
