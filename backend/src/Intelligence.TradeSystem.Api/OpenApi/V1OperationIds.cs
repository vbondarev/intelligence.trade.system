namespace Intelligence.TradeSystem.Api.OpenApi;

internal static class V1OperationIds
{
    private static readonly Dictionary<string, string> Catalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET api/v1/auth/me"] = "getCurrentUser",
            ["GET api/v1/me/exchange-accounts"] = "listExchangeAccounts",
            ["POST api/v1/me/exchange-accounts"] = "createExchangeAccount",
            ["PATCH api/v1/me/exchange-accounts/{id}"] = "renameExchangeAccount",
            ["POST api/v1/me/exchange-accounts/{id}/verify"] = "verifyExchangeAccount",
            ["PUT api/v1/me/exchange-accounts/{id}/credentials"] = "rotateExchangeAccountCredentials",
            ["DELETE api/v1/me/exchange-accounts/{id}"] = "disconnectExchangeAccount",
            ["POST api/v1/exchange-accounts/{id}/sync"] = "syncExchangeAccount",
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
