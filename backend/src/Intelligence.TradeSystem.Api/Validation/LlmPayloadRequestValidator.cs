using FluentValidation;
using Intelligence.TradeSystem.Api.Contracts;

namespace Intelligence.TradeSystem.Api.Validation;

/// <summary>
/// Валидатор query-параметров запроса <c>GET /api/market-analysis/{symbol}/llm-payload</c>.
/// Route-параметр <c>symbol</c> не входит в этот DTO и проверяется в контроллере отдельно.
/// </summary>
public sealed class LlmPayloadRequestValidator : AbstractValidator<LlmPayloadRequest>
{
    public LlmPayloadRequestValidator()
    {
        RuleFor(x => x.Exchange)
            .NotNull()
            .WithMessage("Field 'exchange' is required.");

        RuleFor(x => x.Category)
            .NotNull()
            .WithMessage("Field 'category' is required.");
    }
}
