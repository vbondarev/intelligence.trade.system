# Product Capability Map

Capability Map фиксирует текущие и будущие возможности Intelligence.TradeSystem независимо от порядка их реализации.

Порядок строк и разделов **не является ROADMAP**. Единственным источником утверждённой последовательности разработки остаётся [ROADMAP.md](../../ROADMAP.md).

## Статусы зрелости

| Статус | Значение |
|---|---|
| Idea | Идея зафиксирована, но ещё не прошла предметное исследование. |
| Research | Изучаются пользовательская ценность, рынок, ограничения или техническая реализуемость. |
| Concept | Продуктовая модель в целом понятна, но capability ещё не утверждена к реализации. |
| Planned | Capability принята как конкретная задача или часть утверждённой последовательности реализации в ROADMAP. Простое упоминание в `Future Product Directions` или среди возможных направлений Stage M само по себе не переводит capability в `Planned`. |
| In Development | Capability находится в активной реализации. |
| Available | Capability реализована в согласованной пользовательской или платформенной границе. |
| Deferred | Capability сознательно отложена до выполнения зависимостей или отдельного решения. |
| Rejected | Принято решение не развивать capability в рассматриваемой форме. |

Переход Idea / Research / Concept → Planned требует отдельного human decision, результатом которого становится конкретное включение capability в утверждённую implementation sequence ROADMAP. Упоминание направления как возможного будущего развития не считается таким переходом.

## Portfolio & Position Intelligence

| Capability | Ценность | Зависимости | Статус | Риски / примечания |
|---|---|---|---|---|
| Биржевые аккаунты и read-only sync | Даёт системе фактическое состояние счёта и позиций | Exchange adapters, identity, persistence | Available | Первый MVP ограничивает credentials правами чтения |
| Portfolio management | Позволяет видеть состояние и риск портфеля | Account sync, PortfolioState, Web UI | In Development | Первый MVP account-scoped; cross-account analytics позже |
| Position monitoring | Централизует состояние активных позиций | Position model, sync, Web UI | In Development | UI и realtime ещё развиваются |
| Position assessment | Даёт воспроизводимую оценку позиции | Market Intelligence, portfolio context, policies | Available | Требует свежих и согласованных inputs |
| Deterministic recommendation | Предлагает проверяемое действие и причины | Assessment, RecommendationPolicy | Available | AI не может подменять policy |
| Continuous monitoring | Снимает необходимость ручной постоянной проверки | Background reevaluation, events, notifications | Planned | Конкретно запланирован Stage H (`H-01` — `H-06`); нужно контролировать частоту, freshness и noise |

## AI Intelligence Layer

| Capability | Ценность | Зависимости | Статус | Риски / примечания |
|---|---|---|---|---|
| AI Explanation | Объясняет готовую рекомендацию на нужном пользователю уровне | Assessment, Recommendation, reason codes | Planned | Конкретно запланирован Stage I (`I-05`/`I-06`); LLM не меняет решение backend, нужен deterministic fallback |
| AI Copilot | Даёт единый естественно-языковой интерфейс к продукту | Стабильный user API, Web/BFF, safe tools | Concept | User isolation, tool authorization, audit, prompt injection |
| Context Synthesis | Объединяет новости, macro, social, on-chain и market context | Source adapters, provenance, freshness model | Concept | Достоверность источников и шум |
| Personalization | Адаптирует объяснения и предложения под пользователя | Profile, Journal, explicit preferences | Concept | Нельзя скрыто менять risk policy пользователя |
| AI Trading Coach | Объясняет закономерности в истории торговли | Trading Journal, outcome statistics | Concept | Нужен достаточный объём данных и защита от ложных выводов |
| AI Research Agent | Исследует отобранные рыночные возможности | Market Screener, research context | Concept | Не должен заменять детерминированный первичный screening |
| AI Bot Analyst | Анализирует эффективность торговых ботов и режимы рынка | Bot Journal, Market Intelligence | Idea | Требуется надёжная идентификация bot-originated activity |
| AI Action Agent | Подготавливает структурированные торговые намерения | TradeIntent, Risk Gateway, execution preview | Concept | Не должен исполнять действие в обход risk validation |
| Controlled Agentic Trading | Ограниченное автономное исполнение | Quality evidence, paper/testnet, risk validator, kill switch | Deferred | Высокие финансовые и операционные риски |

## Trading Intelligence

| Capability | Ценность | Зависимости | Статус | Риски / примечания |
|---|---|---|---|---|
| Trader Journal | Формирует проверяемую историю торговли пользователя | Position lifecycle, timeline, closed trades | Concept | Stage M упоминает торговый журнал только как возможное направление расширения; конкретная implementation sequence ещё не утверждена |
| Journal analytics | Показывает PnL, MAE/MFE, duration, risk и статистику сетапов | Trader Journal, calculation model | Concept | Требуется согласованная методика метрик |
| Conversational journal queries | Позволяет задавать вопросы своей истории естественным языком | Journal analytics, AI Copilot | Concept | Ответы должны ссылаться на фактические данные |
| AI Trading Coach | Выявляет повторяющиеся ошибки и сильные стороны | Journal analytics, достаточная история | Concept | Статистические паттерны не должны выдаваться за причинность |

## Market Intelligence

| Capability | Ценность | Зависимости | Статус | Риски / примечания |
|---|---|---|---|---|
| Market Screener | Находит инструменты по формальным рыночным условиям | Instrument universe, batching, caching, feature computation | Concept | Rate limits, стоимость массового анализа, latency |
| Saved Screeners | Позволяет сохранять и повторно использовать условия поиска | Market Screener, user persistence | Concept | Версионирование условий и совместимость |
| AI Screener | Переводит естественный язык в формальный ScreenerDefinition | Market Screener, structured output validation | Concept | AI не должен напрямую подменять scanner engine |
| AI Research Agent | Глубоко анализирует небольшой набор отобранных кандидатов | Screener results, Context Synthesis | Concept | Стоимость, provenance, воспроизводимость |

## Social & Strategy Intelligence

| Capability | Ценность | Зависимости | Статус | Риски / примечания |
|---|---|---|---|---|
| Trader Profile | Даёт представление о стиле и проверяемой истории трейдера | Journal, identity/privacy model | Idea | Privacy и представление статистики риска |
| Follow Trader | Позволяет наблюдать за действиями выбранного трейдера | Trader Profile, publication model | Idea | Контроль того, какие данные публикуются |
| Strategy | Позволяет отделять торговую стратегию от личности трейдера | Journal classification, strategy identity | Idea | Требуется определить lifecycle и ownership |
| Verified Trading History | Показывает результаты, построенные самой системой | Exchange facts, Journal, immutable history | Concept | Нельзя позволять удалять невыгодные факты из track record |
| Shadow Copy | Проверяет копирование на виртуальном портфеле без реальных ордеров | Published intents, CopyPolicy, simulation | Concept | Реалистичность моделирования исполнения |
| Internal Copy Trading | Копирует торговое намерение через персональный Risk Gateway follower | TradeIntent, execution layer, risk policies | Concept | Financial safety, synchronization, partial failures |
| Strategy subscriptions | Даёт подписку на отдельную стратегию, а не всего трейдера | Strategy, publication, access model | Idea | Billing/entitlements вне текущего scope |

## Automation & Execution

| Capability | Ценность | Зависимости | Статус | Риски / примечания |
|---|---|---|---|---|
| GinArea monitoring | Связывает позиции, созданные ботами, с общим Portfolio/Journal | Exchange observation, source attribution | Idea | Прямая integration API GinArea должна исследоваться отдельно |
| Bot Journal | Сравнивает результат бота с рынком, риском и рекомендациями системы | Journal, bot attribution | Idea | Нужна корректная группировка bot activity |
| AI Bot Builder | Предлагает конфигурацию внешнего бота | Market Intelligence, bot capability model | Idea | Предложение не равно разрешению на запуск |
| External execution integrations | Позволяют исполнять TradeIntent через внешние системы | ExecutionProvider abstraction, risk validation | Idea | API availability, auth, partial failure, audit |
| Controlled execution | Выполняет ограниченный набор действий после validation/confirmation | Stage N foundations, risk validator | Deferred | Не допускается до проверки качества рекомендаций |

## Связи между направлениями

Некоторые capabilities являются надстройками над базовыми продуктами:

- AI Trading Coach строится поверх Trader Journal и Journal analytics.
- AI Research Agent строится поверх Market Screener и Context Synthesis.
- AI Copilot является общим интерфейсом к Portfolio, Market, Journal и Social capabilities.
- Internal Copy Trading использует Social/Strategy слой, но фактическая допустимость действия определяется персональным Risk Gateway follower.
- GinArea и другие execution providers не заменяют Portfolio/Position Intelligence: после открытия позиция сопровождается общей системой.
- Controlled Agentic Trading является поздней стадией Automation & Execution, а не обязательным продолжением любого AI capability.
