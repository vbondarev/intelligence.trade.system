# ADR-0004: Единый DB-backed runtime и readiness API

## Статус

Принято.

## Контекст

`Intelligence.TradeSystem.Api` обслуживает пользовательские сценарии поверх business persistence. Частично собранный DB-less composition root создавал состояние, в котором процесс API был запущен, но backend не мог полноценно обслуживать пользовательские сценарии.

## Решение

- API имеет только один runtime-режим: полноценный DB-backed backend.
- PostgreSQL и `ConnectionStrings:TradeSystem` являются обязательной инфраструктурной конфигурацией API.
- Публичный market-analysis работает внутри этого же API и не образует отдельный DB-less режим.
- Отсутствующая или некорректная обязательная конфигурация приводит к fail-fast при startup.
- Временная недоступность PostgreSQL отражается как failed readiness, но не требует завершения процесса.
- `/alive` проверяет только liveness процесса и не зависит от PostgreSQL.
- `/healthz` включает PostgreSQL health check и отражает готовность API.
- Startup не выполняет connectivity gate к PostgreSQL.
- API не применяет EF Core migrations автоматически; schema operations выполняются отдельно.

Расширенные проверки Bybit и фоновых jobs остаются частью отдельного этапа L-03.

## Последствия

Тестовые и deployment-конфигурации API должны предоставлять обязательную persistence и credential protection configuration. Compose smoke проверяет отдельно liveness и readiness. Новые runtime modes для отключения business persistence не добавляются.
