namespace Intelligence.TradeSystem.Infrastructure.Security;

/// <summary>
/// Controlled failure for invalid, unavailable, or tampered credential protection data.
/// </summary>
public sealed class CredentialProtectionException : Exception
{
    public CredentialProtectionException(string message)
        : base(message)
    {
    }
}
