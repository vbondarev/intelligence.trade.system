---
name: trade-system-discovery
description: Проводит Discovery идеи или проблемы до согласования scope, требований и критериев приёмки в GitHub Issue.
---

# Discovery

## Граница workflow

Каноническая последовательность:

`Discussion / Research → Human decisions → GitHub Issue`

Входом может быть идея, проблема, пункт `ROADMAP.md`, технический долг или запрос на исследование без согласованного Issue. Discovery заканчивается согласованным GitHub Issue; этот skill не составляет Implementation Plan, не создаёт branch и не начинает implementation.

## Анализ

Изучи фактическое состояние репозитория и применимые источники:

- актуальную target branch и `ROADMAP.md`;
- корневые, локальные и path-specific instructions;
- product documentation, ADR и contract docs;
- релевантный код, связанный Issue и Pull Request.

Сформулируй цель и контекст, requirements, scope, out-of-scope, архитектурные вопросы, риски и проверяемые acceptance criteria. Не копируй в Issue правила, уже определённые применимыми инструкциями, ADR или contracts.

## Human decisions и stop conditions

Не принимай самостоятельно новое продуктовое или архитектурное решение и не расширяй scope. Остановись и передай вопрос человеку, если остаётся неоднозначность, конфликт источников, новый архитектурный выбор или решение влияет на scope/acceptance criteria.

Создавай или синхронизируй Issue только после явного согласования человеком. После этого Issue является source of truth для `WHAT`.

## Классификация Issue

Назначай ровно один `type:*` и один или несколько фактически применимых `area:*`:

- `type:feature` — новая функциональность;
- `type:bug` — исправление некорректного поведения;
- `type:refactor` — внутренняя структура без намеренного изменения поведения;
- `type:documentation` — документация и repository instructions;
- `type:maintenance` — CI, tooling и техническое обслуживание.

Доступные области: `area:domain`, `area:application`, `area:api`, `area:market-intelligence`, `area:infrastructure`, `area:exchange`, `area:identity`, `area:frontend`, `area:platform`, `area:ci`, `area:agent-workflow`, `area:openclaw`. Не назначай область только потому, что задача использует существующую возможность этого слоя.

Добавляй labels `impact:breaking-change`, `impact:performance`, `risk:security` и `follow-up` только при соответствующем смысле. Labels классифицируют Issue и не являются workflow state; Pull Request наследует актуальные labels связанного Issue.
