---
name: trade-system-pr-review
description: Выполняет технический review Pull Request Intelligence.TradeSystem по текущему head, Issue, Approved Plan, архитектурным правилам, тестам, CI и замечаниям Codex/Copilot.
---

# Технический review pull request

Перед анализом обязательно сверь:

1. актуальные PR head/base SHA и полный diff;
2. связанный GitHub Issue;
3. Approved Implementation Plan;
4. применимые root/local/path-specific instructions;
5. применимые ADR и contract docs.

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

Self-review автора не заменяет этот External Review. Не выполняй merge без отдельного Human Merge Gate.
