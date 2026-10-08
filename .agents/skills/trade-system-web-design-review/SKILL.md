---
name: trade-system-web-design-review
description: Проводит воспроизводимый UI/UX/accessibility review React Web-клиента по локально зафиксированным Web Interface Guidelines с приоритетом repository instructions.
---

# Web UI review

## Назначение и граница

Specialized project-owned skill для UI/UX/accessibility review `frontend/intelligence-trade-web`. Он не является стадией agent-first lifecycle `Discovery → Delivery → External Review` и применяется только когда задача или review действительно касается frontend UI: разметки, стилей, взаимодействия, доступности, адаптивности или пользовательских текстов.

Review выполняется только по явно заданным файлам или diff. Без такого scope остановись и уточни его; не расширяй review на весь frontend по собственной инициативе.

## Источники и приоритет

Прочитай в этом порядке:

1. корневой `AGENTS.md`;
2. `frontend/intelligence-trade-web/AGENTS.md`;
3. `docs/frontend-architecture.md`;
4. локальный snapshot [Web Interface Guidelines](guidelines.md).

Приоритет: Issue и Approved Implementation Plan текущей задачи → repository и frontend instructions → frontend architecture и contracts → `guidelines.md`. Generic guideline не переопределяет принятое решение проекта.

`guidelines.md` — неизменённая копия upstream `command.md` с pinned revision; происхождение и лицензия описаны в `.agents/skills/README.md` и в `LICENSE` рядом. Используй только этот локальный файл: не загружай guidelines из сети и не сверяйся с текущим upstream `main`. Placeholder `$ARGUMENTS` в snapshot означает файлы или diff, заданные для этого review.

## Применимость правил

Проверяй только правила, применимые к React + TypeScript + Vite SPA с собственным CSS. Нерелевантные или technology-specific правила пропускай без finding, например:

- Tailwind-классы (`focus-visible:ring-*`, `truncate`, `min-w-0`) — проверяй эквивалентное поведение в CSS, а не наличие классов;
- hydration safety, `priority` и другие SSR/Next.js-specific рекомендации — в клиенте нет server rendering;
- рекомендации конкретных библиотек (`nuqs`, `virtua` и подобные) — это не разрешение добавить зависимость;
- требования к стилю текста английского UI (Title Case, curly quotes `“ ”`) — интерфейс русскоязычный, применяй нормы русского текста;
- «URL reflects state» — URL-state применяется по модели state ownership из `docs/frontend-architecture.md`, а не к любому состоянию.

## Ограничения

Review не разрешает и не предлагает как обязательные:

- новые dependencies, UI framework, design system или Storybook;
- изменение frontend architecture, BFF/API contracts или state-management подхода;
- реализацию G-07 «Адаптивность и базовая доступность» или G-08 «Базовая дизайн-система и обработка ошибок»;
- соседний refactoring вне заданного scope.

Finding — это наблюдение, а не команда исправления. Решение об исправлении принимается в рамках текущей задачи, её Approved Plan или отдельного Issue.

## Формат результата

Группируй findings по файлам в terse формате `file:line - проблема`, как описано в разделе `Output Format` локального snapshot. Для файла без findings укажи `✓ pass`. Если finding противоречит repository instruction или выходит за scope задачи, не включай его как finding; при необходимости отметь отдельно как возможный follow-up.
