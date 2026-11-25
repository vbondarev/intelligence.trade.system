---
name: trade-system-pr-review
description: Выполняет технический review Pull Request Intelligence.TradeSystem по текущему head, Issue, Approved Plan, архитектурным правилам, тестам, CI и замечаниям Codex/Copilot.
---

# Технический review pull request

Перед анализом восстанови согласованное состояние из GitHub, без опоры на предыдущую AI-сессию:

1. PR body и его единственную primary Issue link `Closes #<issue>`;
2. связанный GitHub Issue;
3. применимое основание исполнения: permalink доверенного base Plan и все действующие Amendments либо разрешённый `N/A`;
4. на обычном маршруте — Amendments в порядке Issue comments и их ссылки на active base Plan;
5. на обычном маршруте — immutable timestamps (`created_at == updated_at`) canonical comments;
6. актуальные PR head/base SHA и полный diff;
7. применимые root/local/path-specific instructions, ADR и contract docs.

На обычном маршруте canonical HOW принимается только от `vbondarev` с `author_association=OWNER`. Отсутствующая или неоднозначная Issue/Plan-связь, битый permalink, ссылка на другую Issue/repository, untrusted comment, неперечисленный amendment, изменённый canonical artifact или amendment без ссылки на active base Plan являются blocker workflow defects. Если canonical HOW изменён/удалён или его невозможно восстановить, остановись и запроси Human Decision; не угадывай актуальный Plan по случайным comments Issue.

Для `Approved Implementation Plan: N/A` проверь `Approved Plan Amendments:` с `- Нет`, связь PR с Issue, применимость точечного маршрута по `AGENTS.md`, отсутствие доверенных плана и Amendments и проверяемое явное разрешение владельца для этого Issue до реализации. Отсутствие основания или существенное расширение объёма блокирует маршрут. Для малого FIX в запланированном PR используй действующий permalink.

Затем:

1. Получи current HEAD/BASE, linked Issue, применимое основание, полный diff и exact-head CI.
2. Зафиксируй reviewed-state fingerprint: PR, HEAD/BASE, ссылку на Issue, permalink плана и Amendments в Issue order либо `N/A` и доказательство разрешения владельца, exact-head CI run/checks.
3. Проверь фактический текущий код, а не результаты прошлого review.
4. Проверь архитектурные границы, correctness, security и user isolation. Если затронуты persistence/concurrency или API contracts, проверь их отдельно; также проверь tests и документацию, когда PR меняет заявленное состояние проекта.
5. Обязательно проверь текущие Codex reviews/comments, Copilot reviews/comments и inline threads.
6. Не принимай замечания bots автоматически: каждое проверь по текущему коду и классифицируй как `valid` или `invalid`, а также как `blocker` или `non-blocker`.
7. Перед выводом о готовности заново подтверди HEAD/BASE, Issue link и основание, включая Amendments либо доказательство разрешения `N/A`.
8. При изменении HEAD/BASE, основания, существенных PR metadata или появлении потенциального blocker finding обнови GitHub state и выбери применимый маршрут ниже.
9. Если состояние изменилось во время проверки, обнови его и затронутые выводы перед вердиктом.
10. Перед Human Merge Gate подтверди покрытие текущего состояния независимой проверкой, зелёный exact-head CI и отсутствие unresolved blockers. Для новой пары требуется актуальный №12.
11. В итоговом review явно укажи Blockers, Non-blockers, Codex status, Copilot status, review threads status, CI status и merge readiness.
12. Без явного разрешения не выполняй merge, push, изменение кода, публикацию review comments или resolve threads.
13. Если пользователь разрешил закрыть исправленные review threads, сначала докажи по текущему коду, что замечание больше не актуально, затем resolve только конкретный thread.
14. Если PR не относится к OpenClaw, учитывай freeze и не анализируй `openclaw/**` как часть основной реализации.
15. Проверь соблюдение языковой политики: человекочитаемый текст, XML documentation, комментарии и review-пояснения должны быть на русском языке; общеупотребимые технические термины и идентификаторы не переводи. Сверяй изменённый текст с исходным смыслом и контрактами: перевод не должен удалять идентификаторы, искажать wire values, условия, инварианты или смысл комментария.

`Merge readiness = READY` не разрешает merge. Merge выполняется после отдельного Human Merge Gate для проверенного состояния.

## Выбор проверки перед merge

- При актуальном положительном №8 без обязательных FIX переходи к №11 и №12 с опорой на охват отчёта №8.
- После №11 выполняй независимую проверку внутри №12 §4B, если изменились только документы, BASE прежняя, уточнение следует из утверждённых WHAT/HOW и критерии приёмки сохраняются. Проверь основание, дельту, полный текущий diff, затронутые выводы, HEAD/BASE и обязательный CI. Ошибка только в тексте возвращается в №11. Изменение обязательств требует решения по WHAT/HOW.
- При установленном исходном ревью опубликованный FIX ведёт к №10, вход А. Существенная смена BASE, кода, технического контракта или действующего HOW без FIX ведёт к №10, вход Б. Если есть оба основания, учти оба.
- Если применимого исходного ревью не было, выполни №8 на актуальном состоянии. Если отчёт мог существовать, восстанови его охват и версию. Пока они не подтверждены, результат №12 — `UNVERIFIED`.

## Review fixes и Re-review

После External Review не считай подтверждённые findings автоматически исправленными:

1. Каждое замечание сначала проверь по актуальному head, Issue, Approved Plan, применимым instructions, ADR/contracts и tests.
2. Если пользователь разрешил изменения, исправляй подтверждённые findings в существующей branch/PR; при необходимости обнови tests и документацию и повтори применимые checks.
3. После review fixes выполни self-review затронутого изменения и полного diff. Исправленный или устаревший thread объясняй и resolve только после проверки нового head и только при наличии разрешения на публикацию/resolve.
4. Если finding требует нового architecture decision, scope expansion или противоречит Issue/Approved Plan/contract, остановись и верни вопрос на Human Gate. После решения синхронизируй source-of-truth и повтори Gate для затронутого изменения.
5. После существенных review fixes обязателен Re-review актуального нового head: заново получи head/base/diff, Issue и применимое основание, актуальные comments/threads и CI именно этого head. Предыдущий External Review и старый CI не подтверждают новый head.
6. К Human Merge Gate переходи после применимой независимой проверки, обработки blockers и положительного №12.

Self-review автора не заменяет этот External Review. Не выполняй merge без отдельного Human Merge Gate.
