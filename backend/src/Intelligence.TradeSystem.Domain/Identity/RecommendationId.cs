namespace Intelligence.TradeSystem.Domain.Identity;

public readonly record struct RecommendationId
{
    public Guid Value { get; }

    private RecommendationId(Guid value) => Value = value;

    public static RecommendationId New() => new(Guid.NewGuid());

    public static RecommendationId FromGuid(Guid value) => value != Guid.Empty
        ? new(value)
        : throw new ArgumentException("RecommendationId value cannot be Guid.Empty.", nameof(value));

    public override string ToString() => Value.ToString();
}
