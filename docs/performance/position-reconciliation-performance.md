# Данные производительности PostgreSQL: согласование позиций

Дата измерения: 2026-10-22T17:29:37.7869871+00:00
Образ PostgreSQL: `postgres:16-alpine`
Версия PostgreSQL: `PostgreSQL 16.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 15.2.0) 15.2.0, 64-bit`
Docker Engine: `29.7.2`
Среда теста: Microsoft Windows 10.0.26300; .NET 10.0.11; SDK 10.0.400; CPU threads 16; доступная память 33,618,251,776 bytes.
Настройки PostgreSQL: `shared_buffers=128MB`, `work_mem=4MB`, `effective_cache_size=4GB`, `max_parallel_workers=8`, `random_page_cost=4`.

Для каждого workflow и варианта реализации выполнен прогрев (1) и 3 измерения после прогрева. В таблице приведены медианы трёх измерений; полные результаты каждого прогона и SQL приведены в JSON evidence.
В JSON полный SQL хранится один раз в `sqlStatements`; каждая запись измерения ссылается на него через `sqlStatementId` и содержит параметры конкретного выполнения.
Materialized rows считает позиции, PositionChanges, portfolio projections, watermarks и результаты latest-closed lookup, включая все lightweight watermark rows и историю, повторно загруженную для append-only CAS verification. Account, cursor и outbox строки сюда не входят; их SQL-команды и длительности учитываются.
Baseline воспроизводит последовательность `develop`: lightweight watermark всех lifecycle до locks → position locks → account lock → полная SQL-материализация через `GetByExchangeAccountAsync` после locks, включая закрытые lifecycle и `PositionChanges`. Адаптер повторно использует только эту post-lock materialization. Optimized читает current watermark/projections, требуемую target/current history и latest closed slot. Проверки подтверждают SQL-порядок по перехваченным командам; измеряется весь workflow.
Обе реализации используют одинаковые seeded positions, историю, workflow observations и Application write paths. После измерений тест сравнивает outcomes, current aggregate histories и сохранённый portfolio state.
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

| Набор | Сценарий | Базовый: SQL round trips / position-state materialized rows / query ms / wall ms | Оптимизированный: SQL round trips / position-state materialized rows / query ms / wall ms | Заблокировано position rows (базовый → оптимизированный) | Position lock SQL ms (базовый → оптимизированный) | Account lock SQL ms (базовый → оптимизированный) | Lock holding ms (базовый → оптимизированный) |
|---|---|---:|---:|---:|---:|---:|---:|
| 10/100 | Наблюдение wallet | 22 / 134 / 28.197 / 63.010 | 21 / 14 / 28.291 / 60.719 | 17 → 7 | 1.193 → 1.785 | 1.460 → 1.325 | 57.796 → 55.291 |
| 10/100 | Прямое обновление позиции | 23 / 137 / 27.381 / 56.224 | 24 / 21 / 28.540 / 59.977 | 17 → 7 | 1.385 → 1.391 | 0.932 → 0.986 | 50.708 → 54.944 |
| 10/100 | Открытие после закрытия | 21 / 140 / 22.881 / 57.248 | 21 / 19 / 21.441 / 50.465 | 19 → 9 | 1.738 → 1.467 | 0.918 → 1.059 | 50.846 → 47.075 |
| 10/100 | Полная REST reconciliation | 49 / 149 / 57.670 / 119.236 | 50 / 40 / 56.894 / 123.490 | 17 → 7 | 1.172 → 1.319 | 1.260 → 1.108 | 111.843 → 116.184 |
| 10/100 | Блокировки position/account | 2 / 0 / 3.163 / 4.490 | 2 / 0 / 2.711 / 4.201 | 17 → 7 | 1.756 → 1.665 | 1.170 → 1.046 | 4.011 → 3.714 |
| 1 000/10 000 | Наблюдение wallet | 22 / 12014 / 29.708 / 155.756 | 21 / 14 / 22.855 / 54.690 | 1007 → 7 | 3.030 → 1.365 | 1.258 → 1.113 | 148.062 → 51.154 |
| 1 000/10 000 | Прямое обновление позиции | 23 / 12017 / 34.862 / 166.241 | 24 / 21 / 34.878 / 70.804 | 1007 → 7 | 3.269 → 1.262 | 1.208 → 1.066 | 159.218 → 64.892 |
| 1 000/10 000 | Открытие после закрытия | 21 / 12020 / 32.918 / 160.596 | 21 / 19 / 24.259 / 63.968 | 1009 → 9 | 3.124 → 1.522 | 1.580 → 1.149 | 151.704 → 59.310 |
| 1 000/10 000 | Полная REST reconciliation | 49 / 12029 / 59.511 / 192.684 | 50 / 40 / 52.657 / 114.080 | 1007 → 7 | 3.661 → 1.421 | 1.077 → 1.197 | 183.326 → 106.685 |
| 1 000/10 000 | Блокировки position/account | 2 / 0 / 3.593 / 4.921 | 2 / 0 / 2.961 / 4.431 | 1007 → 7 | 2.418 → 1.639 | 1.193 → 1.110 | 4.628 → 4.095 |
| 10 000/100 000 | Наблюдение wallet | 22 / 120014 / 101.365 / 646.006 | 21 / 14 / 18.097 / 39.362 | 10007 → 7 | 9.531 → 1.067 | 1.445 → 1.238 | 633.365 → 35.277 |
| 10 000/100 000 | Прямое обновление позиции | 23 / 120017 / 112.450 / 563.652 | 24 / 21 / 24.995 / 44.847 | 10007 → 7 | 10.524 → 1.137 | 1.099 → 0.851 | 543.843 → 41.589 |
| 10 000/100 000 | Открытие после закрытия | 21 / 120020 / 103.538 / 675.922 | 21 / 19 / 21.887 / 47.056 | 10009 → 9 | 10.919 → 1.194 | 1.231 → 1.084 | 663.185 → 43.419 |
| 10 000/100 000 | Полная REST reconciliation | 49 / 120029 / 142.238 / 662.588 | 50 / 40 / 57.400 / 104.568 | 10007 → 7 | 10.951 → 1.430 | 1.232 → 1.011 | 641.309 → 97.502 |
| 10 000/100 000 | Блокировки position/account | 2 / 0 / 10.977 / 12.329 | 2 / 0 / 2.089 / 3.103 | 10007 → 7 | 9.417 → 1.197 | 1.778 → 0.762 | 12.071 → 2.921 |

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
