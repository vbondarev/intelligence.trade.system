namespace Intelligence.TradeSystem.MarketIntelligence.Valuation;

public static class LinearPositionIndicativeValueCalculator
{
    public static decimal? Calculate(decimal size, decimal markPrice)
    {
        if (size <= 0m || markPrice <= 0m)
        {
            return null;
        }

        try
        {
            return size * markPrice;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
