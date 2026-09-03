namespace Intelligence.TradeSystem.Domain.Decisions;

/// <summary>Итог будущей полной проверки рекомендации увеличить существующую позицию.</summary>
public enum AddDecision
{
    NotEvaluated,
    DoNotAdd,
    AddAllowed
}
