using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Проверяет маппинг метаданных уровней поддержки/сопротивления в LLM payload.
/// Инварианты:
/// - ненулевой уровень → соответствующий *Meta присутствует в JSON
/// - нулевой уровень → *Meta отсутствует в JSON
/// - Price совпадает с плоским полем
/// - Source = "volume-profile" (единственный детектор V1)
/// - Strength = 0.7 ∈ [0, 1]
/// - DistancePct совпадает с distanceToSupport1Pct / distanceToResistance1Pct для уровней 1
/// </summary>
public sealed class LlmLevelMetaMappingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Url = "/api/market-analysis/BTCUSDT/llm-payload?exchange=Bybit&category=Linear";

    private readonly WebApplicationFactory<Program> _factory;

    public LlmLevelMetaMappingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── support1Meta присутствует и корректен ───────────────────────────────

    [Fact]
    public async Task Support1Meta_Is_Present_When_Support1_Is_NonZero()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support1Meta.Should().NotBeNull(
                because: $"{tf.Timeframe}: support1={tf.Support1} is non-zero → support1Meta must be present");
        });
    }

    [Fact]
    public async Task Support1Meta_Price_Matches_Flat_Support1()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support1Meta!.Price.Should().Be(tf.Support1,
                because: $"{tf.Timeframe}: support1Meta.price must equal flat support1 field");
        });
    }

    [Fact]
    public async Task Support1Meta_Source_Is_VolumeProfile()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support1Meta!.Source.Should().Be("volume-profile",
                because: $"{tf.Timeframe}: only SimplifiedVolumeProfile detector is used in V1");
        });
    }

    [Fact]
    public async Task Support1Meta_Strength_Matches_Snapshot_Support1Strength()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support1Meta!.Strength.Should().Be(ApiSnapshotTestData.Support1StrengthValue,
                because: $"{tf.Timeframe}: support1Meta.strength must equal snapshot.Support1Strength");
        });
    }

    [Fact]
    public async Task Support1Meta_StrengthLabel_Matches_Expected_Label()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            // Support1Strength = 0.9 → Strong
            tf.Support1Meta!.StrengthLabel.Should().Be("Strong",
                because: $"{tf.Timeframe}: Support1Strength=0.9 >= 0.70 → label must be Strong");
        });
    }

    [Fact]
    public async Task Support1Meta_DistancePct_Matches_DistanceToSupport1Pct()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support1Meta!.DistancePct.Should().Be(tf.DistanceToSupport1Pct,
                because: $"{tf.Timeframe}: support1Meta.distancePct must equal distanceToSupport1Pct flat field");
        });
    }

    // ─── resistance1Meta присутствует и корректен ────────────────────────────

    [Fact]
    public async Task Resistance1Meta_Is_Present_When_Resistance1_Is_NonZero()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Resistance1Meta.Should().NotBeNull(
                because: $"{tf.Timeframe}: resistance1={tf.Resistance1} is non-zero → resistance1Meta must be present");
        });
    }

    [Fact]
    public async Task Resistance1Meta_Price_Matches_Flat_Resistance1()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Resistance1Meta!.Price.Should().Be(tf.Resistance1,
                because: $"{tf.Timeframe}: resistance1Meta.price must equal flat resistance1 field");
        });
    }

    [Fact]
    public async Task Resistance1Meta_DistancePct_Matches_DistanceToResistance1Pct()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Resistance1Meta!.DistancePct.Should().Be(tf.DistanceToResistance1Pct,
                because: $"{tf.Timeframe}: resistance1Meta.distancePct must equal distanceToResistance1Pct flat field");
        });
    }

    // ─── support2Meta и resistance2Meta присутствуют ─────────────────────────

    [Fact]
    public async Task Support2Meta_Is_Present_When_Support2_Is_NonZero()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support2Meta.Should().NotBeNull(
                because: $"{tf.Timeframe}: support2={tf.Support2} is non-zero → support2Meta must be present");
        });
    }

    [Fact]
    public async Task Resistance2Meta_Is_Present_When_Resistance2_Is_NonZero()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Resistance2Meta.Should().NotBeNull(
                because: $"{tf.Timeframe}: resistance2={tf.Resistance2} is non-zero → resistance2Meta must be present");
        });
    }

    // ─── нулевые уровни → Meta отсутствует в JSON ────────────────────────────

    [Fact]
    public async Task Support1Meta_Is_Absent_From_Json_When_Support1_Is_Null()
    {
        var snapshot = CreateSnapshotWithNullLevels();
        using var client = CreateClientWithSnapshot(snapshot);
        using var response = await client.GetAsync(Url);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var m15 = json.RootElement.GetProperty("m15");

        m15.TryGetProperty("support1Meta", out _).Should().BeFalse(
            because: "support1==null → support1Meta must not appear in JSON");
        m15.TryGetProperty("support2Meta", out _).Should().BeFalse(
            because: "support2==null → support2Meta must not appear in JSON");
        m15.TryGetProperty("resistance1Meta", out _).Should().BeFalse(
            because: "resistance1==null → resistance1Meta must not appear in JSON");
        m15.TryGetProperty("resistance2Meta", out _).Should().BeFalse(
            because: "resistance2==null → resistance2Meta must not appear in JSON");
    }

    // ─── StrengthLabel присутствует и содержит допустимое значение ───────────

    [Theory]
    [InlineData("support1Meta")]
    [InlineData("support2Meta")]
    [InlineData("resistance1Meta")]
    [InlineData("resistance2Meta")]
    public async Task LevelMeta_StrengthLabel_Is_Valid_Enum_Value(string metaField)
    {
        var validLabels = new[] { "Strong", "Moderate", "Weak", "Unavailable" };
        var result = await GetPayloadAsync();

        foreach (var tf in new[] { result!.M15, result.H1, result.H4, result.D1 })
        {
            var meta = metaField switch
            {
                "support1Meta" => tf.Support1Meta,
                "support2Meta" => tf.Support2Meta,
                "resistance1Meta" => tf.Resistance1Meta,
                "resistance2Meta" => tf.Resistance2Meta,
                _ => null,
            };

            if (meta?.StrengthLabel is { } label)
            {
                validLabels.Should().Contain(label,
                    because: $"{tf.Timeframe}.{metaField}: strengthLabel must be one of {string.Join(", ", validLabels)}");
            }
        }
    }

    // ─── Strength в диапазоне [0, 1] ─────────────────────────────────────────

    [Theory]
    [InlineData("support1Meta")]
    [InlineData("support2Meta")]
    [InlineData("resistance1Meta")]
    [InlineData("resistance2Meta")]
    public async Task LevelMeta_Strength_Is_In_Range_0_To_1(string metaField)
    {
        var result = await GetPayloadAsync();

        foreach (var tf in new[] { result!.M15, result.H1, result.H4, result.D1 })
        {
            var meta = metaField switch
            {
                "support1Meta" => tf.Support1Meta,
                "support2Meta" => tf.Support2Meta,
                "resistance1Meta" => tf.Resistance1Meta,
                "resistance2Meta" => tf.Resistance2Meta,
                _ => null,
            };

            if (meta?.Strength is { } strength)
            {
                strength.Should().BeInRange(0m, 1m,
                    because: $"{tf.Timeframe}.{metaField}: strength must be in [0, 1]");
            }
        }
    }

    // ─── Консистентность: Meta.Price == плоское поле ─────────────────────────

    [Theory]
    [InlineData("Support2")]
    [InlineData("Resistance2")]
    public async Task LevelMeta_Price_Matches_Flat_Field_For_Level2(string levelName)
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            if (levelName == "Support2" && tf.Support2Meta != null)
            {
                tf.Support2Meta.Price.Should().Be(tf.Support2,
                    because: $"{tf.Timeframe}: support2Meta.price must equal support2 flat field");
            }
            else if (levelName == "Resistance2" && tf.Resistance2Meta != null)
            {
                tf.Resistance2Meta.Price.Should().Be(tf.Resistance2,
                    because: $"{tf.Timeframe}: resistance2Meta.price must equal resistance2 flat field");
            }
        });
    }

    // ─── ClusterVolume присутствует и корректен ──────────────────────────────

    [Fact]
    public async Task Support1Meta_ClusterVolume_Matches_Snapshot()
    {
        var result = await GetPayloadAsync();

        AssertAllTimeframes(result!, tf =>
        {
            tf.Support1Meta!.ClusterVolume.Should().Be(ApiSnapshotTestData.Support1ClusterVolumeValue,
                because: $"{tf.Timeframe}: support1Meta.clusterVolume must equal snapshot.Support1ClusterVolume");
        });
    }

    [Fact]
    public async Task LevelMeta_ClusterVolume_Is_Absent_From_Json_When_Level_Is_Null()
    {
        var snapshot = CreateSnapshotWithNullLevels();
        using var client = CreateClientWithSnapshot(snapshot);
        using var response = await client.GetAsync(Url);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var m15 = json.RootElement.GetProperty("m15");

        // clusterVolume must not appear when the level itself is absent
        m15.TryGetProperty("support1Meta", out _).Should().BeFalse(
            because: "support1==null → support1Meta (and its clusterVolume) must not appear in JSON");
    }

    [Theory]
    [InlineData("support1Meta")]
    [InlineData("support2Meta")]
    [InlineData("resistance1Meta")]
    [InlineData("resistance2Meta")]
    public async Task LevelMeta_ClusterVolume_Is_Positive_When_Level_Is_Present(string metaField)
    {
        var result = await GetPayloadAsync();

        foreach (var tf in new[] { result!.M15, result.H1, result.H4, result.D1 })
        {
            var meta = metaField switch
            {
                "support1Meta" => tf.Support1Meta,
                "support2Meta" => tf.Support2Meta,
                "resistance1Meta" => tf.Resistance1Meta,
                "resistance2Meta" => tf.Resistance2Meta,
                _ => null,
            };

            if (meta is not null)
            {
                meta.ClusterVolume.Should().HaveValue(
                    because: $"{tf.Timeframe}.{metaField}: clusterVolume must be present when level is detected");
                meta.ClusterVolume!.Value.Should().BePositive(
                    because: $"{tf.Timeframe}.{metaField}: cluster volume must be > 0");
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<LlmMarketAnalysisPayload?> GetPayloadAsync()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        using var client = CreateClientWithSnapshot(snapshot);
        using var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<LlmMarketAnalysisPayload>();
    }

    private HttpClient CreateClientWithSnapshot(MarketAnalysisSnapshot snapshot)
    {
        var mock = new Mock<IMarketAnalysisService>(MockBehavior.Strict);
        mock.Setup(x => x.BuildSnapshotAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<string>(),
                It.IsAny<MarketCategory>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        return _factory.CreateClientWithMarketAnalysisService(mock.Object);
    }

    private static void AssertAllTimeframes(
        LlmMarketAnalysisPayload result,
        Action<LlmTimeframePayload> assertion)
    {
        foreach (var tf in new[] { result.M15, result.H1, result.H4, result.D1 })
            assertion(tf);
    }

    /// <summary>Снапшот с null-уровнями — имитирует ситуацию, когда детектор не нашёл уровней.</summary>
    private static MarketAnalysisSnapshot CreateSnapshotWithNullLevels()
    {
        var baseSnapshot = ApiSnapshotTestData.CreateSnapshot();

        var nullTimeframe = baseSnapshot.M15 with
        {
            Support1 = null,
            Support2 = null,
            Resistance1 = null,
            Resistance2 = null,
            DistanceToSupport1Pct = null,
            DistanceToResistance1Pct = null,
        };

        return baseSnapshot with
        {
            M15 = nullTimeframe,
            H1 = nullTimeframe with { Timeframe = "1h" },
            H4 = nullTimeframe with { Timeframe = "4h" },
            D1 = nullTimeframe with { Timeframe = "1d" },
        };
    }
}
