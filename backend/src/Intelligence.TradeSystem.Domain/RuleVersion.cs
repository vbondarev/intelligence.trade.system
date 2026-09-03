namespace Intelligence.TradeSystem.Domain;

public readonly record struct RuleVersion
{
    public string Value { get; }

    public RuleVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public static RuleVersion From(string value) => new(value);

    public override string ToString() => Value;
}
