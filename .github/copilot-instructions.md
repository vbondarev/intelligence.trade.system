# Инструкции GitHub Copilot

Для постоянных repository rules используй корневой `AGENTS.md`. Дополняй его применимыми локальными `AGENTS.md` и path-specific instructions из `.github/instructions/`.

Для C# дополнительно применяй `backend/src/AGENTS.md`. Актуальное состояние и последовательность этапов определяй по `ROADMAP.md`, а требования конкретной задачи — по связанному GitHub Issue и Approved Implementation Plan.

Выбирай project-owned workflow skill по стадии:

- до согласованного Issue — `.agents/skills/trade-system-discovery/SKILL.md`;
- после Issue до PR sanity check — `.agents/skills/trade-system-delivery/SKILL.md`;
- для External Review — `.agents/skills/trade-system-pr-review/SKILL.md`.

Следуй явно указанной иерархии instructions и routing; не предполагай скрытого vendor-specific precedence между инструкциями и skills.
