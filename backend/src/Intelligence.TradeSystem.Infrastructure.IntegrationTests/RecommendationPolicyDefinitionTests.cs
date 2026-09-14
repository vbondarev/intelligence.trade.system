using System.Text.Json.Nodes;
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
              "version":"recommendation-v1","validityPeriod":"00:05:00","closeLossThreshold":-10,"reduceLossThreshold":-5,"protectProfitThreshold":2,"takePartialProfitThreshold":5,"confidenceProfiles":{"hold":0.70,"watch":0.80,"protectProfit":0.85,"reduce":0.90,"close":0.98,"moveStop":0.88,"takePartialProfit":0.86},"priorityProfiles":{"hold":"normal","watch":"normal","protectProfit":"high","reduce":"high","close":"critical","moveStop":"high","takePartialProfit":"high"},"addAllowedLimits":{"maximumAdditionalPositionPercentOfEquity":10,"maximumAdditionalAvailableCapitalPercent":25,"minimumLiquidationDistancePercent":5},"reevaluationProfile":{"hold":"00:04:00","watch":"00:02:00","protectProfit":"00:02:00","reduce":"00:01:00","close":"00:01:00","moveStop":"00:02:00","takePartialProfit":"00:02:00","addAllowed":"00:02:00"},"stabilityProfile":{"minimumReplacementInterval":"00:00:30","improvementConfirmationPeriod":"00:02:00","improvementConfirmationObservations":2,"addAllowedConfirmationPeriod":"00:03:00","addAllowedConfirmationObservations":3}}
            """);
        var secondPath = WritePolicy(
            """
            {
              "addAllowedLimits": {
                "minimumLiquidationDistancePercent": 5,
                "maximumAdditionalAvailableCapitalPercent": 25,
                "maximumAdditionalPositionPercentOfEquity": 10
              },
              "reevaluationProfile": {
                "addAllowed": "00:02:00",
                "takePartialProfit": "00:02:00",
                "moveStop": "00:02:00",
                "close": "00:01:00",
                "reduce": "00:01:00",
                "protectProfit": "00:02:00",
                "watch": "00:02:00",
                "hold": "00:04:00"
              },
              "stabilityProfile": {
                "addAllowedConfirmationObservations": 3,
                "addAllowedConfirmationPeriod": "00:03:00",
                "improvementConfirmationObservations": 2,
                "improvementConfirmationPeriod": "00:02:00",
                "minimumReplacementInterval": "00:00:30"
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
    public void Unknown_property_fails_fast()
    {
        var document = DefaultDocument();
        document["unknown"] = JsonValue.Create(true);
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Invalid_ttl_fails_fast()
    {
        var document = DefaultDocument();
        document["validityPeriod"] = JsonValue.Create("00:00:00");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Missing_stability_profile_fails_fast()
    {
        var document = DefaultDocument();
        document.Remove("stabilityProfile");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Missing_stability_profile_property_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document).Remove("minimumReplacementInterval");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Missing_improvement_confirmation_period_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document).Remove("improvementConfirmationPeriod");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Missing_improvement_confirmation_observations_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document).Remove("improvementConfirmationObservations");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Missing_add_allowed_confirmation_period_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document).Remove("addAllowedConfirmationPeriod");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Missing_add_allowed_confirmation_observations_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document).Remove("addAllowedConfirmationObservations");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Unknown_stability_profile_property_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document)["unknown"] = JsonValue.Create(true);
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Invalid_stability_duration_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document)["improvementConfirmationPeriod"] = JsonValue.Create("00:00:00");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Negative_stability_duration_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document)["minimumReplacementInterval"] = JsonValue.Create("-00:00:30");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Invalid_stability_observation_count_fails_fast()
    {
        var document = DefaultDocument();
        StabilityProfile(document)["addAllowedConfirmationObservations"] = JsonValue.Create(0);
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Invalid_threshold_order_fails_fast()
    {
        var document = DefaultDocument();
        document["closeLossThreshold"] = JsonValue.Create(-1);
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Invalid_confidence_fails_fast()
    {
        var document = DefaultDocument();
        document["confidenceProfiles"]!.AsObject()["hold"] = JsonValue.Create(1.1);
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Invalid_add_limit_fails_fast()
    {
        var document = DefaultDocument();
        document["addAllowedLimits"]!.AsObject()["maximumAdditionalPositionPercentOfEquity"] =
            JsonValue.Create(0);
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Unknown_priority_enum_fails_fast()
    {
        var document = DefaultDocument();
        document["priorityProfiles"]!.AsObject()["close"] = JsonValue.Create("urgent");
        AssertInvalid(document.ToJsonString());
    }

    [Fact]
    public void Integer_priority_enum_fails_fast()
    {
        var document = DefaultDocument();
        document["priorityProfiles"]!.AsObject()["close"] = JsonValue.Create(1);
        AssertInvalid(document.ToJsonString());
    }

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
        var document = DefaultDocument();
        document.Remove("version");
        AssertInvalid(document.ToJsonString());
    }

    private static string WritePolicy(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"recommendation-policy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static JsonObject DefaultDocument() =>
        JsonNode.Parse(DefaultJson)!.AsObject();

    private static JsonObject StabilityProfile(JsonObject document) =>
        document["stabilityProfile"]!.AsObject();

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
          },
          "reevaluationProfile": {
            "hold": "00:04:00",
            "watch": "00:02:00",
            "protectProfit": "00:02:00",
            "reduce": "00:01:00",
            "close": "00:01:00",
            "moveStop": "00:02:00",
            "takePartialProfit": "00:02:00",
            "addAllowed": "00:02:00"
          },
          "stabilityProfile": {
            "minimumReplacementInterval": "00:00:30",
            "improvementConfirmationPeriod": "00:02:00",
            "improvementConfirmationObservations": 2,
            "addAllowedConfirmationPeriod": "00:03:00",
            "addAllowedConfirmationObservations": 3
          }
        }
        """;
}
