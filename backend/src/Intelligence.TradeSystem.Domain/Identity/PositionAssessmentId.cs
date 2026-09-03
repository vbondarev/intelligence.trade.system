namespace Intelligence.TradeSystem.Domain.Identity;

public readonly record struct PositionAssessmentId
{
    public Guid Value { get; }

    private PositionAssessmentId(Guid value) => Value = value;

    public static PositionAssessmentId New() => new(Guid.NewGuid());

    public static PositionAssessmentId FromGuid(Guid value) => value != Guid.Empty
        ? new(value)
        : throw new ArgumentException("PositionAssessmentId value cannot be Guid.Empty.", nameof(value));

    public override string ToString() => Value.ToString();
}
