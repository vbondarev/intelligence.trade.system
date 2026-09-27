---
name: trade-system-delivery
description: Выполняет согласованную Issue-задачу от анализа и Approved Implementation Plan до Draft PR и PR sanity check.
---

# Delivery

## Граница workflow

Каноническая последовательность:

`Issue → Implementation Plan → Human Gate → branch → implementation → self-review → checks → docs → final self-review → commit → push → Draft PR → PR sanity`

После PR sanity передай работу в `.agents/skills/trade-system-pr-review/SKILL.md`; там выполняется полный цикл `External Review → Review fixes → Re-review → Human Merge Gate`. Не дублируй здесь детали этого цикла.

## Plan и Human Gate

До Plan изучи связанный Issue, `ROADMAP.md`, применимые `AGENTS.md` и path-specific instructions, ADR, contracts, релевантный код и подходящие skills. Plan должен определить компоненты, последовательность, архитектурные последствия, проверки, документацию, риски и границы scope.

Для нетривиальной задачи implementation запрещён до явного Human Gate — утверждения Plan человеком. Остановись и верни вопрос человеку при новом архитектурном выборе, scope expansion, конфликте Issue/Plan/ADR/contract или необходимости изменить согласованный `WHAT`. Не стандартизируй хранение Approved Plan и durable workflow state в обход отдельного решения.

После Gate повторно получи актуальную target branch и создай отдельную ветку вида `task/<issue>-<kebab-case>`. Реализуй только согласованные Issue `WHAT` и Approved Plan `HOW`; соседние улучшения не добавляй.

## Implementation и проверки

Перед commit выполни основной self-review полного diff относительно target branch. Сверь реализацию с Issue и Approved Plan, архитектурными границами, backward compatibility, security/concurrency, tests, документацией, случайными файлами, debug-кодом и secrets; исправь подтверждённые замечания.

Выполни применимые checks. Неприменимые проверки не называй успешными и кратко укажи причину. Синхронизируй документы, ставшие неверными из-за реализации. После fixes/tests/docs проведи final self-review полного итогового diff.

## Commit, Draft PR и sanity check

- Commit message: `#<issue>: <текст на русском языке в прошедшем времени>`.
- PR title: `#<issue>: <краткое название на русском языке>`.
- PR description пиши по-русски; включи `Closes #<issue>`, фактическую реализацию, отклонения от Approved Plan или явное указание об их отсутствии, выполненные проверки, риски и намеренно исключённый scope.
- Pull Request наследует актуальные classification labels связанного Issue и не вводит другую классификацию самостоятельно. Если новая область означает scope expansion или новое architecture decision, остановись и верни вопрос на Human Gate до изменения Issue/labels.
- До External Review проверь base/head, branch name, commit message, PR title/body, `Closes`, совпадение labels с Issue, полный diff, случайные файлы и применимый CI.
