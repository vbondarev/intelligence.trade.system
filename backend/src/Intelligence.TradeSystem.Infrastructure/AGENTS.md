# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.Infrastructure` и дополняет `../AGENTS.md` только правилами persistence и других infrastructure implementations.

## Ответственность и границы

- Infrastructure реализует EF Core/PostgreSQL persistence, repositories и технические реализации Application ports.
- Проект зависит от `Application` и `Domain`; не переносить EF Core entities, конфигурацию или persistence concerns в Domain/Application.
- Repository layer сохраняет бизнес-инварианты, но не дублирует и не подменяет Domain/Application business rules.

## Persistence и migrations

- EF Core entities и mapping остаются деталями Infrastructure и не становятся Domain model.
- Изменения PostgreSQL schema выполняются через migrations; синхронизируй migration, model snapshot и текущую модель.
- Не применяй migrations автоматически при старте API.

## Transactions и concurrency

- Сохраняй согласованные transaction boundaries для persistence изменений и атомарность связанных записей.
- Persistence-side optimistic concurrency/CAS и version tokens остаются деталями Infrastructure; не протаскивай их в Domain только ради хранения.
- PostgreSQL locks и conditional writes должны реализовывать общие lock-order, state re-read, conflict и bounded-retry invariants из `../AGENTS.md`, а не вводить собственный порядок блокировок.
- При обновлении после lock повторно проверяй актуальное состояние, чтобы исключить stale writes.

## Credentials и outbox

- Exchange credentials хранятся в защищённом зашифрованном виде; расшифрованные значения используются только как transient inputs.
- Не логируй credentials, key material или другие secrets и не помещай их в публичные contracts.
- Сохраняй атомарную запись outbox вместе с соответствующим бизнес-состоянием.
- Outbox обеспечивает at-least-once delivery: повторная доставка возможна, поэтому consumers должны быть к ней устойчивы.

## Проверки

Изменения persistence, transactions, concurrency, migrations или credential storage проверяй через `Intelligence.TradeSystem.Infrastructure.IntegrationTests` с реальным PostgreSQL/Testcontainers.
