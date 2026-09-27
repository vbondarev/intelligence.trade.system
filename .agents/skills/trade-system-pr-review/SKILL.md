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
5. immutable timestamps (`created_at == updated_at`) canonical comments и разрешённую Integrity Incident → replacement chain;
6. актуальный `plan-freshness` status именно для PR head SHA, затем актуальные PR head/base SHA и полный diff;
7. применимые root/local/path-specific instructions, ADR и contract docs.

Canonical HOW принимается только от `vbondarev` с `author_association=OWNER`; `github-actions[bot]` доверяется только для Integrity Incident. Изменённый/удалённый artifact без корректного Incident и immutable replacement, отсутствующая или неоднозначная Issue/Plan-связь, битый permalink, ссылка на другую Issue/repository, untrusted comment, неперечисленный amendment или amendment без ссылки на active base Plan являются blocker workflow defects. Не угадывай актуальный Plan по случайным comments Issue.

Затем:

1. Проверяй фактический текущий код, а не результаты прошлого review.
2. Проверь архитектурные границы, correctness, security и user isolation. Если затронуты persistence/concurrency или API contracts, проверь их отдельно; также проверь tests и документацию, когда PR меняет заявленное состояние проекта.
3. Обязательно проверь текущие Codex reviews/comments, Copilot reviews/comments и inline threads.
4. Не принимай замечания bots автоматически: каждое проверь по текущему коду и классифицируй как `valid` или `invalid`, а также как `blocker` или `non-blocker`.
5. Проверь CI именно для текущего head SHA. Старый CI не является доказательством готовности текущего head.
6. В итоговом review явно укажи Blockers, Non-blockers, Codex status, Copilot status, review threads status, CI status и merge readiness.
7. Без явного разрешения не выполняй merge, push, изменение кода, публикацию review comments или resolve threads.
8. Если пользователь разрешил закрыть исправленные review threads, сначала докажи по текущему коду, что замечание больше не актуально, затем resolve только конкретный thread.
9. Если PR не относится к OpenClaw, учитывай freeze и не анализируй `openclaw/**` как часть основной реализации.
10. Проверь соблюдение языковой политики: человекочитаемый текст, XML documentation, комментарии и review-пояснения должны быть на русском языке; общеупотребимые технические термины и идентификаторы не переводи. Сверяй изменённый текст с исходным смыслом и контрактами: перевод не должен удалять идентификаторы, искажать wire values, условия, инварианты или смысл комментария.

## Review fixes и Re-review

После External Review не считай подтверждённые findings автоматически исправленными:

1. Каждое замечание сначала проверь по актуальному head, Issue, Approved Plan, применимым instructions, ADR/contracts и tests.
2. Если пользователь разрешил изменения, исправляй подтверждённые findings в существующей branch/PR; при необходимости обнови tests и документацию и повтори применимые checks.
3. После review fixes выполни self-review затронутого изменения и полного diff. Исправленный или устаревший thread объясняй и resolve только после проверки нового head и только при наличии разрешения на публикацию/resolve.
4. Если finding требует нового architecture decision, scope expansion или противоречит Issue/Approved Plan/contract, остановись и верни вопрос на Human Gate. После решения синхронизируй source-of-truth и повтори Gate для затронутого изменения.
5. После существенных review fixes обязателен Re-review актуального нового head: заново получи head/base/diff, проверь актуальные comments/threads и CI именно этого head. Предыдущий External Review и старый CI не подтверждают новый head.
6. К Human Merge Gate переходи только после завершения требуемого Re-review и обработки подтверждённых blocker findings.

Self-review автора не заменяет этот External Review. Не выполняй merge без отдельного Human Merge Gate.
