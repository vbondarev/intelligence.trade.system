---
name: trade-system-pr-review
description: Выполняет технический review Pull Request Intelligence.TradeSystem по текущему head, Issue, Approved Plan, архитектурным правилам, тестам, CI и замечаниям Codex/Copilot.
---

# Технический review pull request

Перед анализом восстанови согласованное состояние из GitHub, без опоры на предыдущую AI-сессию:

1. PR body и его единственную primary Issue link `Closes #<issue>`;
2. связанный GitHub Issue;
3. permalink в поле `Approved Implementation Plan` и trusted base comment `# Approved Implementation Plan` этой Issue;
4. все перечисленные trusted Approved Plan Amendments в порядке Issue comments, с их ссылками на active base Plan;
5. immutable timestamps (`created_at == updated_at`) canonical comments;
6. актуальные PR head/base SHA и полный diff;
7. применимые root/local/path-specific instructions, ADR и contract docs.

Canonical HOW принимается только от `vbondarev` с `author_association=OWNER`. Отсутствующая или неоднозначная Issue/Plan-связь, битый permalink, ссылка на другую Issue/repository, untrusted comment, неперечисленный amendment, изменённый canonical artifact или amendment без ссылки на active base Plan являются blocker workflow defects. Если canonical HOW изменён/удалён или его невозможно восстановить, остановись и запроси Human Decision; не угадывай актуальный Plan по случайным comments Issue.

Затем:

1. Получи current head SHA и current base SHA, затем linked Issue, trusted immutable base Plan, все trusted immutable Amendments в Issue-comment order, current diff и exact-head CI.
2. Зафиксируй reviewed-state fingerprint: PR number, head SHA, base SHA, base Plan permalink, Amendment permalinks в Issue order и exact-head CI run/checks.
3. Проверь фактический текущий код, а не результаты прошлого review.
4. Проверь архитектурные границы, correctness, security и user isolation. Если затронуты persistence/concurrency или API contracts, проверь их отдельно; также проверь tests и документацию, когда PR меняет заявленное состояние проекта.
5. Обязательно проверь текущие Codex reviews/comments, Copilot reviews/comments и inline threads.
6. Не принимай замечания bots автоматически: каждое проверь по текущему коду и классифицируй как `valid` или `invalid`, а также как `blocker` или `non-blocker`.
7. Перед verdict `Merge readiness = READY` заново подтверди head SHA, base SHA, Issue link, base Plan link, Amendment list и effective HOW.
8. Предыдущий review инвалидируется при изменении PR head SHA, base SHA, PR Issue/Plan/Amendment metadata, canonical base Plan, Amendment set/order или появлении нового потенциального blocker finding.
9. Если что-либо изменилось во время review: `STOP → refresh GitHub state → repeat applicable Re-review`.
10. Перед Human Merge Gate проверь current head/base/Plan/Amendment list совпадающими с reviewed fingerprint, exact-head CI green и отсутствие новых unresolved blockers. Любое изменение требует нового Re-review и нового Human Merge Gate.
11. В итоговом review явно укажи Blockers, Non-blockers, Codex status, Copilot status, review threads status, CI status и merge readiness.
12. Без явного разрешения не выполняй merge, push, изменение кода, публикацию review comments или resolve threads.
13. Если пользователь разрешил закрыть исправленные review threads, сначала докажи по текущему коду, что замечание больше не актуально, затем resolve только конкретный thread.
14. Если PR не относится к OpenClaw, учитывай freeze и не анализируй `openclaw/**` как часть основной реализации.
15. Проверь соблюдение языковой политики: человекочитаемый текст, XML documentation, комментарии и review-пояснения должны быть на русском языке; общеупотребимые технические термины и идентификаторы не переводи. Сверяй изменённый текст с исходным смыслом и контрактами: перевод не должен удалять идентификаторы, искажать wire values, условия, инварианты или смысл комментария.

`Merge readiness = READY` не разрешает merge. Merge запрещён без отдельного Human Merge Gate; если перед Gate изменилось reviewed state, сначала проведи новый live Re-review.

## Review fixes и Re-review

После External Review не считай подтверждённые findings автоматически исправленными:

1. Каждое замечание сначала проверь по актуальному head, Issue, Approved Plan, применимым instructions, ADR/contracts и tests.
2. Если пользователь разрешил изменения, исправляй подтверждённые findings в существующей branch/PR; при необходимости обнови tests и документацию и повтори применимые checks.
3. После review fixes выполни self-review затронутого изменения и полного diff. Исправленный или устаревший thread объясняй и resolve только после проверки нового head и только при наличии разрешения на публикацию/resolve.
4. Если finding требует нового architecture decision, scope expansion или противоречит Issue/Approved Plan/contract, остановись и верни вопрос на Human Gate. После решения синхронизируй source-of-truth и повтори Gate для затронутого изменения.
5. После существенных review fixes обязателен Re-review актуального нового head: заново получи head/base/diff, Issue/Plan/Amendments, актуальные comments/threads и CI именно этого head. Предыдущий External Review и старый CI не подтверждают новый head.
6. К Human Merge Gate переходи только после завершения требуемого Re-review и обработки подтверждённых blocker findings.

Self-review автора не заменяет этот External Review. Не выполняй merge без отдельного Human Merge Gate.
