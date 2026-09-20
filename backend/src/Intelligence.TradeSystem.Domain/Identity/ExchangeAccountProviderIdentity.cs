namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Exchange-neutral identity of the provider-side account bound to an exchange account.
/// </summary>
public readonly record struct ExchangeAccountProviderIdentity
{
    public const int MaxLength = 128;

    public string Value { get; }

    public ExchangeAccountProviderIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Provider identity cannot exceed {MaxLength} characters.",
                nameof(value));
        }

        Value = value;
    }

    public static ExchangeAccountProviderIdentity From(string value) => new(value);

    public override string ToString() => Value;
}
