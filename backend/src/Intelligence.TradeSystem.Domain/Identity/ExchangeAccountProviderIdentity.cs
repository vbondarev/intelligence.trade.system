namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Нейтральная к бирже identity аккаунта на стороне provider, связанного с биржевым аккаунтом.
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
