---
name: trade-system-pr-review
description: Проверяет pull request Intelligence.TradeSystem по текущему head, архитектурным границам, тестам, CI и замечаниям Codex/Copilot. Использовать при полном техническом review PR перед слиянием.
---

# Технический review pull request

1. Получи актуальные PR head SHA, base SHA и список изменённых файлов с diff.
2. Проверяй фактический текущий код, а не результаты прошлого review.
3. Проверь архитектурные границы, correctness, security и user isolation. Если затронуты persistence/concurrency или API contracts, проверь их отдельно; также проверь tests и документацию, когда PR меняет заявленное состояние проекта.
4. Обязательно проверь текущие Codex reviews/comments, Copilot reviews/comments и inline threads.
5. Не принимай замечания bots автоматически: каждое проверь по текущему коду и классифицируй как `valid` или `invalid`, а также как `blocker` или `non-blocker`.
6. Проверь CI именно для текущего head SHA. Старый CI не является доказательством готовности текущего head.
7. В итоговом review укажи блокеры, неблокирующие замечания, Codex status, Copilot status, CI status и merge readiness.
8. Без явного разрешения не выполняй merge, push, изменение кода, публикацию review comments или resolve threads.
9. Если пользователь разрешил закрыть исправленные review threads, сначала докажи по текущему коду, что замечание больше не актуально, затем resolve только конкретный thread.
10. Если PR не относится к OpenClaw, учитывай freeze и не анализируй `openclaw/**` как часть основной реализации.
