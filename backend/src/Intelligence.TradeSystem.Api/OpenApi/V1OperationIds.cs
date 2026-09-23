namespace Intelligence.TradeSystem.Api.OpenApi;

internal static class V1OperationIds
{
    private static readonly Dictionary<string, string> Catalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET api/v1/auth/me"] = "getCurrentUser",
            ["GET api/v1/exchange-accounts"] = "listExchangeAccounts",
            ["POST api/v1/exchange-accounts"] = "createExchangeAccount",
            ["POST api/v1/exchange-accounts/{id}/verify"] = "verifyExchangeAccount",
            ["PUT api/v1/exchange-accounts/{id}/credentials"] = "rotateExchangeAccountCredentials",
            ["POST api/v1/exchange-accounts/{id}/sync"] = "syncExchangeAccount",
            ["DELETE api/v1/exchange-accounts/{id}"] = "disconnectExchangeAccount",
            ["GET api/v1/exchange-accounts/{id}/portfolio"] = "getExchangeAccountPortfolio",
            ["GET api/v1/positions"] = "listPositions",
            ["GET api/v1/positions/{id}"] = "getPosition",
            ["GET api/v1/positions/{id}/market"] = "getPositionMarket",
            ["GET api/v1/positions/{id}/candles"] = "getPositionCandles",
            ["GET api/v1/positions/{id}/evaluation"] = "getPositionEvaluation",
            ["POST api/v1/positions/{id}/evaluation"] = "evaluatePosition",
            ["GET api/v1/positions/{id}/timeline"] = "getPositionTimeline",
        };

    public static bool TryGet(
        string? method,
        string? relativePath,
        out string operationId)
    {
        var key = $"{method?.ToUpperInvariant()} {relativePath?.Trim('/')}";
        return Catalog.TryGetValue(key, out operationId!);
    }
}
