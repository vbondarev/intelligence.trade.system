using FluentValidation;
using Intelligence.TradeSystem.Api.Contracts;

namespace Intelligence.TradeSystem.Api.Validation;

/// <summary>
/// Валидатор тела запроса <c>POST /api/market-analysis/snapshot</c>.
/// Не обрабатывает <c>null</c>-тело — это остаётся на стороне контроллера.
/// </summary>
public sealed class SnapshotAnalysisRequestValidator : AbstractValidator<SnapshotAnalysisRequest>
{
    public SnapshotAnalysisRequestValidator()
    {
        RuleFor(x => x.Exchange)
            .NotNull()
            .WithMessage("Field 'exchange' is required.");

        RuleFor(x => x.Symbol)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("Field 'symbol' is required.");

        RuleFor(x => x.Category)
            .NotNull()
            .WithMessage("Field 'category' is required.");
    }
}
