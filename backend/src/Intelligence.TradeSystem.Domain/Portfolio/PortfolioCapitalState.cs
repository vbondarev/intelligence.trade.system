namespace Intelligence.TradeSystem.Domain.Portfolio;

/// <summary>
/// Нормализованное наблюдение капитала аккаунта, не зависящее от конкретной биржи.
/// </summary>
public sealed record PortfolioCapitalState
{
    public PortfolioCapitalState(
        decimal? totalEquity,
        decimal? availableCapital,
        DateTimeOffset? observedAt,
        decimal? totalWalletBalance = null)
    {
        if (totalEquity is < 0m)
            throw new ArgumentOutOfRangeException(nameof(totalEquity), totalEquity, "Total equity cannot be negative.");

        if (availableCapital is < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(availableCapital), availableCapital, "Available capital cannot be negative.");

        if (totalEquity.HasValue && availableCapital.HasValue && availableCapital > totalEquity)
            throw new ArgumentException(
                "Available capital cannot exceed total equity.", nameof(availableCapital));

        if (totalWalletBalance is < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(totalWalletBalance), totalWalletBalance, "Total wallet balance cannot be negative.");

        TotalEquity = totalEquity;
        AvailableCapital = availableCapital;
        ObservedAt = observedAt;
        TotalWalletBalance = totalWalletBalance;
    }

    public decimal? TotalEquity { get; }
    public decimal? AvailableCapital { get; }
    public DateTimeOffset? ObservedAt { get; }
    public decimal? TotalWalletBalance { get; }
}
