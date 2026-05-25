using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Unit-тесты для <see cref="LevelStrengthLabelMapper"/>.
/// Покрывают все пути: null → Unavailable, граничные значения, консистентность.
/// </summary>
public sealed class LevelStrengthLabelMapperTests
{
    // ─── Null → Unavailable ──────────────────────────────────────────────────

    [Fact]
    public void Null_Strength_Returns_Unavailable()
    {
        LevelStrengthLabelMapper.Map(null)
            .Should().Be(LevelStrengthLabel.Unavailable,
                because: "null strength → источник не поддерживает оценку → Unavailable");
    }

    // ─── Threshold coverage ──────────────────────────────────────────────────

    [Fact]
    public void Strength_1_0_Returns_Strong()
    {
        LevelStrengthLabelMapper.Map(1.0m)
            .Should().Be(LevelStrengthLabel.Strong,
                because: "1.0 — максимальная нормализованная сила → Strong");
    }

    [Fact]
    public void Strength_At_StrongThreshold_Returns_Strong()
    {
        LevelStrengthLabelMapper.Map(LevelStrengthLabelMapper.StrongThreshold)
            .Should().Be(LevelStrengthLabel.Strong,
                because: "strength == StrongThreshold (0.70) должна быть включительной нижней границей Strong");
    }

    [Fact]
    public void Strength_Just_Below_StrongThreshold_Returns_Moderate()
    {
        LevelStrengthLabelMapper.Map(LevelStrengthLabelMapper.StrongThreshold - 0.0001m)
            .Should().Be(LevelStrengthLabel.Moderate,
                because: "strength чуть ниже 0.70 → Moderate");
    }

    [Fact]
    public void Strength_At_ModerateThreshold_Returns_Moderate()
    {
        LevelStrengthLabelMapper.Map(LevelStrengthLabelMapper.ModerateThreshold)
            .Should().Be(LevelStrengthLabel.Moderate,
                because: "strength == ModerateThreshold (0.40) должна быть включительной нижней границей Moderate");
    }

    [Fact]
    public void Strength_Just_Below_ModerateThreshold_Returns_Weak()
    {
        LevelStrengthLabelMapper.Map(LevelStrengthLabelMapper.ModerateThreshold - 0.0001m)
            .Should().Be(LevelStrengthLabel.Weak,
                because: "strength чуть ниже 0.40 → Weak");
    }

    [Fact]
    public void Strength_Zero_Returns_Weak()
    {
        LevelStrengthLabelMapper.Map(0m)
            .Should().Be(LevelStrengthLabel.Weak,
                because: "strength = 0 → минимально возможная сила → Weak");
    }

    // ─── Semantic scenarios ──────────────────────────────────────────────────

    [Fact]
    public void Dominant_Level_Returns_Strong()
    {
        // Нормализованный кластер = 1.0 (доминирующий), метка Strong
        LevelStrengthLabelMapper.Map(1.0m)
            .Should().Be(LevelStrengthLabel.Strong,
                because: "доминирующий кластер strength=1.0 → Strong");
    }

    [Fact]
    public void Secondary_Level_At_Half_Volume_Returns_Weak()
    {
        // Вторичный кластер ≈ 35% от доминирующего → Weak (< 0.40)
        LevelStrengthLabelMapper.Map(0.35m)
            .Should().Be(LevelStrengthLabel.Weak,
                because: "strength=0.35 < ModerateThreshold=0.40 → Weak");
    }

    // ─── Boundary sweep ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.70, "Strong")]
    [InlineData(0.85, "Strong")]
    [InlineData(1.00, "Strong")]
    [InlineData(0.40, "Moderate")]
    [InlineData(0.55, "Moderate")]
    [InlineData(0.699, "Moderate")]
    [InlineData(0.00, "Weak")]
    [InlineData(0.20, "Weak")]
    [InlineData(0.399, "Weak")]
    public void Maps_Strength_To_Correct_Label(double strengthDouble, string expectedLabel)
    {
        var strength = (decimal)strengthDouble;
        var label = LevelStrengthLabelMapper.Map(strength);

        label.ToString().Should().Be(expectedLabel,
            because: $"strength={strength} должна давать метку {expectedLabel}");
    }

    // ─── Консистентность: Unavailable ←→ null ────────────────────────────────

    [Theory]
    [InlineData(0.0,  false)]
    [InlineData(0.5,  false)]
    [InlineData(1.0,  false)]
    public void Unavailable_IFF_Strength_Is_Null(double strengthDouble, bool expectUnavailable)
    {
        var strength = (decimal)strengthDouble;
        var label = LevelStrengthLabelMapper.Map(strength);

        var isUnavailable = label == LevelStrengthLabel.Unavailable;
        isUnavailable.Should().Be(expectUnavailable,
            because: $"Unavailable ←→ strength is null; strength={strength}");
    }

    [Fact]
    public void Non_Null_Strength_Never_Returns_Unavailable()
    {
        foreach (var strength in new[] { 0m, 0.1m, 0.39m, 0.4m, 0.69m, 0.7m, 1.0m })
        {
            LevelStrengthLabelMapper.Map(strength)
                .Should().NotBe(LevelStrengthLabel.Unavailable,
                    because: $"ненулевое strength={strength} не должно давать Unavailable");
        }
    }
}
