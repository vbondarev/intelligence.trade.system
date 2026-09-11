# AGENTS.md

## Область действия

Этот файл применяется к `Intelligence.TradeSystem.Api` и дополняет `../AGENTS.md`. Здесь находятся только HTTP-, payload-, validation- и composition-specific правила API.

## Назначение проекта

`Intelligence.TradeSystem.Api` — client-agnostic resource server и composition root. Контроллеры принимают/валидируют запрос, вызывают Application и преобразуют результат в wire-контракт. Бизнес-правила и торговые вычисления в контроллерах запрещены.

## HTTP и обработка ошибок

- Контроллеры должны оставаться тонкими.
- Используй централизованное преобразование exceptions в `ProblemDetails`; не добавляй ad-hoc error payloads в отдельных endpoints.
- Сохраняй текущую семантику HTTP-кодов и не раскрывай внутренние exceptions/credentials в ответах.
- Authentication/authorization остаются ответственностью middleware и общей API boundary, а не бизнес-контроллеров.
- Не вызывай private exchange APIs из публичных market endpoints.

## Публичные контракты

- Модели в `Models/Payloads` и request/response DTO считай contract-sensitive.
- Предпочитай аддитивное развитие: добавление полей/новых endpoints вместо скрытого переименования, удаления или переосмысления существующих полей.
- JSON enum values сохраняй строковыми согласно общей настройке `Program.cs`; не вводи integer serialization точечно.
- `GET /api/market-analysis/{symbol}/llm-payload` остаётся публичным market-only контрактом со схемой `1.0`, пока версия не меняется отдельным решением.
- Legacy `POST /api/market-analysis/snapshot` сохраняет существующую wire-совместимость до отдельной задачи миграции.
- Не добавляй user/private state в публичные рыночные payloads.

## Разделение с MarketIntelligence

API не вычисляет повторно:

- trend/bias/momentum;
- entry quality;
- risk flags;
- market regime;
- trade-flow pressure;
- market tags;
- indicator values и level strength.

Эта логика принадлежит `Intelligence.TradeSystem.MarketIntelligence`. API только использует готовый результат и выполняет wire mapping.

Подробные алгоритмические правила и пороги не дублируй в этом `AGENTS.md`.

## Свежесть снимка

`Services/SnapshotHealthEvaluator.cs` отвечает за API-level оценку свежести и предупреждения.

Не вводи partial snapshot semantics или новые missing-section правила без согласованного изменения evaluator, payload contract и API tests.

`AnalysisMode` влияет на downstream представление/health evaluation, но не должен менять публичную природу исходного `MarketSnapshot`.

## DI и Program.cs

- Сохраняй `AddServiceDefaults()` и существующую модульную регистрацию через `AddXyz(...)` extensions.
- Не создавай service locator внутри controllers.
- Проверяй lifetime при singleton/scoped взаимодействии; shared background/cache операции не должны захватывать request-scoped dependencies без явного ownership.
- При изменении composition root запускай соответствующие API/architecture tests и полный CI, если затронуты authentication, Docker или инфраструктурные зависимости.

## При изменении кода

Если меняется endpoint или payload:

- обнови request/response models;
- mapper;
- validation;
- `ProblemDetails` assumptions;
- schema/version assumptions;
- `Intelligence.TradeSystem.Api.Tests`.

Если меняется mapping `MarketSnapshot` → LLM payload, проверь `LlmPayloadMapperExtensions` и связанные contract tests.

Если изменение требует новой рыночной логики, реализуй её в `MarketIntelligence`, а не в API.
