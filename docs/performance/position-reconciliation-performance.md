# Данные производительности PostgreSQL: согласование позиций

Дата измерения: 2026-10-21T23:48:10.0023834+00:00
Образ PostgreSQL: `postgres:16-alpine`
Версия PostgreSQL: `PostgreSQL 16.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit`
Docker Engine: `29.7.2`
Среда теста: Microsoft Windows 10.0.26300; .NET 10.0.11; SDK 10.0.400; CPU threads 16; доступная память 33,618,251,776 bytes.
Настройки PostgreSQL: `shared_buffers=128MB`, `work_mem=4MB`, `effective_cache_size=4GB`, `max_parallel_workers=8`, `random_page_cost=4`.

Для каждого workflow и варианта реализации выполнен прогрев (1) и 3 измерения после прогрева. В таблице приведены медианы трёх измерений; полные результаты каждого прогона и SQL приведены в JSON evidence.
Materialized rows считает позиции, PositionChanges, portfolio projections, watermarks и результаты latest-closed lookup, включая историю, повторно загруженную для append-only CAS. Account, cursor и outbox строки сюда не входят; их SQL-команды и длительности учитываются.
Базовый вариант отличается только стратегией чтения: один раз загружает полный legacy aggregate через `GetByExchangeAccountAsync`, включая закрытые lifecycle и `PositionChanges`. Измеряется весь workflow, а не отдельный вызов repository.
Абсолютный SLA не задаётся; длительности зависят от среды. Lock holding включает commit; commit отдельно не измерялся. Диагностические `EXPLAIN ANALYZE` выполняются отдельно в откатываемых транзакциях.

Все полные `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` планы вместе с SQL и параметрами сохранены в [JSON evidence](./position-reconciliation-performance-evidence.json).

## Воспроизведение

Из `backend/src` в PowerShell:

```powershell
$env:ITS_DOCKER_ENGINE_VERSION = docker info --format '{{.ServerVersion}}'
$env:ITS_RUN_POSITION_RECONCILIATION_PERFORMANCE = '1'
dotnet test .\Intelligence.TradeSystem.Infrastructure.IntegrationTests\Intelligence.TradeSystem.Infrastructure.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~PositionReconciliationPerformancePostgreSqlTests" --logger "console;verbosity=minimal"
```

## Наборы данных

| Набор | Закрытые lifecycle | PositionChanges | Текущие позиции после seed |
|---|---:|---:|---:|
| 10/100 | 10 | 100 | Active 5, Unknown 1, Stale 1 |
| 1 000/10 000 | 1,000 | 10,000 | Active 5, Unknown 1, Stale 1 |
| 10 000/100 000 | 10,000 | 100,000 | Active 5, Unknown 1, Stale 1 |

## Сравнение сценариев

| Набор | Сценарий | Базовый: SQL round trips / position-state materialized rows / query ms / wall ms | Оптимизированный: SQL round trips / position-state materialized rows / query ms / wall ms | Заблокировано position rows (базовый → оптимизированный) | Lock SQL ms (базовый → оптимизированный) | Lock holding ms (базовый → оптимизированный) |
|---|---|---:|---:|---:|---:|---:|
| 10/100 | Наблюдение wallet | 21 / 117 / 22.340 / 51.327 | 21 / 14 / 23.916 / 50.383 | 17 → 7 | 2.583 → 2.303 | 42.121 → 43.871 |
| 10/100 | Прямое обновление позиции | 22 / 120 / 22.777 / 49.790 | 24 / 21 / 27.409 / 55.805 | 17 → 7 | 2.108 → 1.981 | 42.256 → 51.988 |
| 10/100 | Открытие после закрытия | 20 / 121 / 20.735 / 49.541 | 21 / 19 / 20.946 / 49.179 | 19 → 9 | 2.696 → 2.248 | 43.072 → 45.998 |
| 10/100 | Полная REST reconciliation | 48 / 132 / 47.364 / 102.708 | 50 / 40 / 49.436 / 102.408 | 17 → 7 | 2.698 → 2.260 | 91.331 → 92.933 |
| 10/100 | Блокировки position/account | 2 / 0 / 2.550 / 4.027 | 2 / 0 / 2.394 / 3.533 | 17 → 7 | 2.550 → 2.394 | 3.609 → 3.150 |
| 1 000/10 000 | Наблюдение wallet | 21 / 11007 / 24.223 / 158.952 | 21 / 14 / 21.368 / 51.409 | 1007 → 7 | 4.662 → 2.216 | 39.038 → 47.312 |
| 1 000/10 000 | Прямое обновление позиции | 22 / 11010 / 27.168 / 142.237 | 24 / 21 / 26.771 / 58.191 | 1007 → 7 | 4.695 → 2.047 | 42.101 → 52.755 |
| 1 000/10 000 | Открытие после закрытия | 20 / 11011 / 24.844 / 172.224 | 21 / 19 / 21.674 / 53.613 | 1009 → 9 | 6.157 → 2.430 | 44.097 → 49.422 |
| 1 000/10 000 | Полная REST reconciliation | 48 / 11022 / 52.861 / 204.236 | 50 / 40 / 49.739 / 105.644 | 1007 → 7 | 4.601 → 2.072 | 99.228 → 98.518 |
| 1 000/10 000 | Блокировки position/account | 2 / 0 / 4.464 / 5.878 | 2 / 0 / 2.388 / 3.527 | 1007 → 7 | 4.464 → 2.388 | 5.331 → 3.225 |
| 10 000/100 000 | Наблюдение wallet | 21 / 110007 / 108.415 / 607.255 | 21 / 14 / 19.859 / 43.193 | 10007 → 7 | 17.625 → 1.751 | 56.204 → 39.623 |
| 10 000/100 000 | Прямое обновление позиции | 22 / 110010 / 100.908 / 595.832 | 24 / 21 / 21.445 / 38.002 | 10007 → 7 | 14.441 → 2.159 | 59.948 → 34.587 |
| 10 000/100 000 | Открытие после закрытия | 20 / 110011 / 93.293 / 583.163 | 21 / 19 / 19.887 / 52.410 | 10009 → 9 | 12.413 → 2.380 | 46.918 → 48.057 |
| 10 000/100 000 | Полная REST reconciliation | 48 / 110022 / 128.625 / 614.655 | 50 / 40 / 45.754 / 86.847 | 10007 → 7 | 12.080 → 2.026 | 93.867 → 79.800 |
| 10 000/100 000 | Блокировки position/account | 2 / 0 / 8.892 / 9.838 | 2 / 0 / 2.280 / 3.243 | 10007 → 7 | 8.892 → 2.280 | 9.645 → 3.061 |

## Использованные индексы PostgreSQL

| Набор | Сценарий | Реализация | Индексы из полных планов |
|---|---|---|---|
| 10/100 | Наблюдение wallet | Baseline | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10/100 | Наблюдение wallet | Optimized | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order`, `ux_positions_active_exchange_key` |
| 10/100 | Прямое обновление позиции | Baseline | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10/100 | Прямое обновление позиции | Optimized | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order`, `ux_positions_active_exchange_key` |
| 10/100 | Открытие после закрытия | Baseline | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10/100 | Открытие после закрытия | Optimized | `PK_portfolio_position_states`, `PK_position_observation_cursors`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_latest_closed_slot`, `ix_positions_list_account_order`, `ux_positions_active_exchange_key` |
| 10/100 | Полная REST reconciliation | Baseline | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10/100 | Полная REST reconciliation | Optimized | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_portfolio_states_account_calculated_at`, `ix_positions_latest_closed_slot`, `ix_positions_list_account_order`, `ux_positions_active_exchange_key` |
| 10/100 | Блокировки position/account | Baseline | `ix_positions_list_account_order` |
| 10/100 | Блокировки position/account | Optimized | `ux_positions_active_exchange_key` |
| 10 000/100 000 | Наблюдение wallet | Baseline | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10 000/100 000 | Наблюдение wallet | Optimized | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ux_positions_active_exchange_key` |
| 10 000/100 000 | Прямое обновление позиции | Baseline | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10 000/100 000 | Прямое обновление позиции | Optimized | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ux_positions_active_exchange_key` |
| 10 000/100 000 | Открытие после закрытия | Baseline | `PK_portfolio_position_states`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10 000/100 000 | Открытие после закрытия | Optimized | `PK_portfolio_position_states`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_latest_closed_slot`, `ux_positions_active_exchange_key` |
| 10 000/100 000 | Полная REST reconciliation | Baseline | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 10 000/100 000 | Полная REST reconciliation | Optimized | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_portfolio_states_account_calculated_at`, `ix_positions_latest_closed_slot`, `ux_positions_active_exchange_key` |
| 10 000/100 000 | Блокировки position/account | Baseline | `ix_positions_list_account_order` |
| 10 000/100 000 | Блокировки position/account | Optimized | `ux_positions_active_exchange_key` |
| 1 000/10 000 | Наблюдение wallet | Baseline | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 1 000/10 000 | Наблюдение wallet | Optimized | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ux_positions_active_exchange_key` |
| 1 000/10 000 | Прямое обновление позиции | Baseline | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 1 000/10 000 | Прямое обновление позиции | Optimized | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ux_positions_active_exchange_key` |
| 1 000/10 000 | Открытие после закрытия | Baseline | `PK_portfolio_position_states`, `PK_position_changes`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 1 000/10 000 | Открытие после закрытия | Optimized | `PK_portfolio_position_states`, `PK_position_observation_cursors`, `PK_positions`, `ix_exchange_rest_ordering_intervals_account_kind_bounds`, `ix_portfolio_states_account_calculated_at`, `ix_positions_latest_closed_slot`, `ux_positions_active_exchange_key` |
| 1 000/10 000 | Полная REST reconciliation | Baseline | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_portfolio_states_account_calculated_at`, `ix_positions_list_account_order` |
| 1 000/10 000 | Полная REST reconciliation | Optimized | `PK_exchange_wallet_observation_cursors`, `PK_portfolio_position_states`, `PK_position_changes`, `PK_positions`, `ix_portfolio_states_account_calculated_at`, `ix_positions_latest_closed_slot`, `ux_positions_active_exchange_key` |
| 1 000/10 000 | Блокировки position/account | Baseline | `ix_positions_list_account_order` |
| 1 000/10 000 | Блокировки position/account | Optimized | `ux_positions_active_exchange_key` |
