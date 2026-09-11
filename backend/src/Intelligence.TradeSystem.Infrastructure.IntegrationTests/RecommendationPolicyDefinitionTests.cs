using Intelligence.TradeSystem.Infrastructure.RecommendationPolicy;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class RecommendationPolicyDefinitionTests
{
    [Fact]
    public async Task Equivalent_json_formatting_and_property_order_has_same_identity()
    {
        var firstPath = WritePolicy(
            """
            {
              "version":"recommendation-v1","validityPeriod":"00:05:00","closeLossThreshold":-10,"reduceLossThreshold":-5,"protectProfitThreshold":2,"takePartialProfitThreshold":5,"confidenceProfiles":{"hold":0.70,"watch":0.80,"protectProfit":0.85,"reduce":0.90,"close":0.98,"moveStop":0.88,"takePartialProfit":0.86},"priorityProfiles":{"hold":"normal","watch":"normal","protectProfit":"high","reduce":"high","close":"critical","moveStop":"high","takePartialProfit":"high"},"addAllowedLimits":{"maximumAdditionalPositionPercentOfEquity":10,"maximumAdditionalAvailableCapitalPercent":25,"minimumLiquidationDistancePercent":5}}
            """);
        var secondPath = WritePolicy(
            """
            {
              "addAllowedLimits": {
                "minimumLiquidationDistancePercent": 5,
                "maximumAdditionalAvailableCapitalPercent": 25,
                "maximumAdditionalPositionPercentOfEquity": 10
              },
              "priorityProfiles": {
                "takePartialProfit": "high",
                "moveStop": "high",
                "close": "critical",
                "reduce": "high",
                "protectProfit": "high",
                "watch": "normal",
                "hold": "normal"
              },
              "confidenceProfiles": {
                "takePartialProfit": 0.86,
                "moveStop": 0.88,
                "close": 0.98,
                "reduce": 0.90,
                "protectProfit": 0.85,
                "watch": 0.80,
                "hold": 0.70
              },
              "takePartialProfitThreshold": 5,
              "protectProfitThreshold": 2,
              "reduceLossThreshold": -5,
              "closeLossThreshold": -10,
              "validityPeriod": "00:05:00",
              "version": "recommendation-v1"
            }
            """);

        try
        {
            var first = new JsonRecommendationPolicyDefinitionProvider(firstPath);
            var second = new JsonRecommendationPolicyDefinitionProvider(secondPath);

            Assert.Equal((await first.GetAsync()).Identity, (await second.GetAsync()).Identity);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public void Unknown_property_fails_fast() =>
        AssertInvalid(DefaultJson.Replace("\n}", ",\n  \"unknown\": true\n}"));

    [Fact]
    public void Invalid_ttl_fails_fast() =>
        AssertInvalid(DefaultJson.Replace("\"validityPeriod\": \"00:05:00\"", "\"validityPeriod\": \"00:00:00\""));

    [Fact]
    public void Invalid_threshold_order_fails_fast() =>
        AssertInvalid(DefaultJson.Replace("\"closeLossThreshold\": -10", "\"closeLossThreshold\": -1"));

    [Fact]
    public void Invalid_confidence_fails_fast() =>
        AssertInvalid(DefaultJson.Replace("\"hold\": 0.7", "\"hold\": 1.1"));

    [Fact]
    public void Invalid_add_limit_fails_fast() =>
        AssertInvalid(DefaultJson.Replace(
            "\"maximumAdditionalPositionPercentOfEquity\": 10",
            "\"maximumAdditionalPositionPercentOfEquity\": 0"));

    [Fact]
    public void Unknown_priority_enum_fails_fast() =>
        AssertInvalid(DefaultJson.Replace("\"close\": \"critical\"", "\"close\": \"urgent\""));

    [Fact]
    public void Integer_priority_enum_fails_fast() =>
        AssertInvalid(DefaultJson.Replace("\"close\": \"critical\"", "\"close\": 1"));

    private static void AssertInvalid(string json)
    {
        var path = WritePolicy(json);

        try
        {
            Assert.ThrowsAny<Exception>(() => new JsonRecommendationPolicyDefinitionProvider(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_version_fails_fast()
    {
        var path = WritePolicy(DefaultJson.Replace("\"version\": \"recommendation-v1\",", string.Empty));
        try
        {
            Assert.ThrowsAny<Exception>(() => new JsonRecommendationPolicyDefinitionProvider(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePolicy(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"recommendation-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string DefaultJson =
        """
        {
          "version": "recommendation-v1",
          "validityPeriod": "00:05:00",
          "closeLossThreshold": -10,
          "reduceLossThreshold": -5,
          "protectProfitThreshold": 2,
          "takePartialProfitThreshold": 5,
          "confidenceProfiles": {
            "hold": 0.7,
            "watch": 0.8,
            "protectProfit": 0.85,
            "reduce": 0.9,
            "close": 0.98,
            "moveStop": 0.88,
            "takePartialProfit": 0.86
          },
          "priorityProfiles": {
            "hold": "normal",
            "watch": "normal",
            "protectProfit": "high",
            "reduce": "high",
            "close": "critical",
            "moveStop": "high",
            "takePartialProfit": "high"
          },
          "addAllowedLimits": {
            "maximumAdditionalPositionPercentOfEquity": 10,
            "maximumAdditionalAvailableCapitalPercent": 25,
            "minimumLiquidationDistancePercent": 5
          }
        }
        """;
}
