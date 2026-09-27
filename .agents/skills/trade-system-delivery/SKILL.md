---
name: trade-system-delivery
description: Выполняет согласованную Issue-задачу от анализа и Approved Implementation Plan до Draft PR и PR sanity check.
---

# Delivery

## Граница workflow

Каноническая последовательность:

`Issue → Implementation Plan → Human Gate → durable Approved Plan → refresh target branch → branch → implementation → self-review → checks → docs → final self-review → commit → push → Draft PR → PR sanity`

После PR sanity передай работу в `.agents/skills/trade-system-pr-review/SKILL.md`; там выполняется полный цикл `External Review → Review fixes → Re-review → Human Merge Gate`. Не дублируй здесь детали этого цикла.

## Plan и Human Gate

До Plan изучи связанный Issue, `ROADMAP.md`, применимые `AGENTS.md` и path-specific instructions, ADR, contracts, релевантный код и подходящие skills. Plan должен определить компоненты, последовательность, архитектурные последствия, проверки, документацию, риски и границы scope.

Для нетривиальной задачи implementation запрещён до явного Human Gate — утверждения Plan человеком. Остановись и верни вопрос человеку при новом архитектурном выборе, scope expansion, конфликте Issue/Plan/ADR/contract или необходимости изменить согласованный `WHAT`.

После Human Gate до обновления target branch и создания branch:

1. Сохрани полный Approved Implementation Plan отдельным комментарием связанного GitHub Issue с точной первой строкой `# Approved Implementation Plan`; canonical comment должен быть опубликован `vbondarev` с `author_association=OWNER`.
2. Получи permalink комментария и проверь, что он доступен для чтения. При ошибке публикации или проверки остановись до исправления состояния.
3. Не редактируй base Plan или Amendment. После human decision и нового Human Gate создай отдельный comment `# Approved Implementation Plan — Amendment` со ссылкой `Base Approved Implementation Plan: <permalink>` и только утверждённым изменением `HOW`.
4. При edit/delete trusted canonical comment дождись Integrity Incident от `github-actions[bot]` и не продолжай с прежним effective Plan. Replacement публикуется отдельным immutable comment после Human Gate с ссылкой на Incident; bot не утверждает Plan.

После успешной проверки permalink повторно получи актуальную target branch и создай отдельную ветку вида `task/<issue>-<kebab-case>`. Реализуй только согласованные Issue `WHAT` и effective Approved Plan `HOW` — trusted immutable base Plan вместе со всеми применимыми trusted amendments и replacements после разрешённых Integrity Incidents. Соседние улучшения не добавляй.

## Implementation и проверки

Перед commit выполни основной self-review полного diff относительно target branch. Сверь реализацию с Issue и Approved Plan, архитектурными границами, backward compatibility, security/concurrency, tests, документацией, случайными файлами, debug-кодом и secrets; исправь подтверждённые замечания.

Выполни применимые checks. Неприменимые проверки не называй успешными и кратко укажи причину. Синхронизируй документы, ставшие неверными из-за реализации. После fixes/tests/docs проведи final self-review полного итогового diff.

## Commit, Draft PR и sanity check

- В PR description добавь permalink базового Approved Plan и permalink каждого применимого amendment либо `- Нет`; отклонения сверяй с effective Approved Plan.
- PR sanity check подтверждает, что base permalink ведёт к immutable comment связанной Issue от `vbondarev/OWNER`, а каждый amendment существует, immutable, доверенно авторизован и ссылается на active base Plan.
- PR sanity check также подтверждает `plan-freshness` status для точного текущего head и наличие этого status context в required checks target branch.

- Commit message: `#<issue>: <текст на русском языке в прошедшем времени>`.
- PR title: `#<issue>: <краткое название на русском языке>`.
- PR description пиши по-русски; включи `Closes #<issue>`, фактическую реализацию, отклонения от Approved Plan или явное указание об их отсутствии, выполненные проверки, риски и намеренно исключённый scope.
- Pull Request наследует актуальные classification labels связанного Issue и не вводит другую классификацию самостоятельно. Если новая область означает scope expansion или новое architecture decision, остановись и верни вопрос на Human Gate до изменения Issue/labels.
- До External Review проверь base/head, branch name, commit message, PR title/body, `Closes`, совпадение labels с Issue, полный diff, случайные файлы и применимый CI.
