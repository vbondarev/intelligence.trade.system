namespace Intelligence.TradeSystem.Web.Security;

/// <summary>
/// Проверяет return URL login flow и защищает от open redirect.
/// </summary>
internal static class LocalReturnUrlValidator
{
    public const string DefaultReturnUrl = "/app";

    private const int MaximumLength = 2048;

    /// <summary>
    /// Принимает только local relative path. Проверка повторяется после percent-decoding,
    /// чтобы закодированные <c>//</c> и <c>\</c> не превращались в внешний адрес.
    /// </summary>
    public static bool TryNormalize(string? returnUrl, out string normalized)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            normalized = DefaultReturnUrl;
            return true;
        }

        normalized = string.Empty;
        if (returnUrl.Length > MaximumLength
            || !IsLocalPath(returnUrl)
            || !IsLocalPath(Uri.UnescapeDataString(returnUrl)))
        {
            return false;
        }

        normalized = returnUrl;
        return true;
    }

    private static bool IsLocalPath(string value) =>
        value.Length > 0
        && value[0] == '/'
        && (value.Length == 1 || value[1] is not ('/' or '\\'))
        && !value.Contains('\\', StringComparison.Ordinal)
        && !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));
}
