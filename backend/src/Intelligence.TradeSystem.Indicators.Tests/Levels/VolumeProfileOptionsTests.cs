using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Levels;

namespace Intelligence.TradeSystem.Indicators.Tests.Levels;

public sealed class VolumeProfileOptionsTests
{
    // ── Default values ───────────────────────────────────────────────────────

    [Fact]
    public void Default_Has_BucketCount_100()
    {
        VolumeProfileOptions.Default.BucketCount.Should().Be(100);
    }

    [Fact]
    public void Default_Has_HvnThresholdRatio_0_7()
    {
        VolumeProfileOptions.Default.HvnThresholdRatio.Should().Be(0.7m);
    }

    [Fact]
    public void Default_Constructor_Produces_Same_Values_As_Default_Singleton()
    {
        var options = new VolumeProfileOptions();

        options.BucketCount.Should().Be(VolumeProfileOptions.Default.BucketCount);
        options.HvnThresholdRatio.Should().Be(VolumeProfileOptions.Default.HvnThresholdRatio);
    }

    // ── Custom valid values ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_Accepts_Custom_BucketCount()
    {
        var options = new VolumeProfileOptions(bucketCount: 50);

        options.BucketCount.Should().Be(50);
        options.HvnThresholdRatio.Should().Be(0.7m); // default
    }

    [Fact]
    public void Constructor_Accepts_Custom_HvnThresholdRatio()
    {
        var options = new VolumeProfileOptions(hvnThresholdRatio: 0.5m);

        options.BucketCount.Should().Be(100); // default
        options.HvnThresholdRatio.Should().Be(0.5m);
    }

    [Fact]
    public void Constructor_Accepts_Both_Custom_Values()
    {
        var options = new VolumeProfileOptions(bucketCount: 200, hvnThresholdRatio: 0.9m);

        options.BucketCount.Should().Be(200);
        options.HvnThresholdRatio.Should().Be(0.9m);
    }

    [Fact]
    public void Constructor_Accepts_HvnThresholdRatio_Of_One()
    {
        // Граничное значение: 1.0 допустимо (только абсолютный максимум является HVN).
        var act = () => new VolumeProfileOptions(hvnThresholdRatio: 1.0m);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_Accepts_Minimal_BucketCount_Of_One()
    {
        // Граничное значение: 1 бакет допустим.
        var act = () => new VolumeProfileOptions(bucketCount: 1);

        act.Should().NotThrow();
    }

    // ── BucketCount validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Throws_ArgumentOutOfRangeException_When_BucketCount_Is_Not_Positive(int bucketCount)
    {
        var act = () => new VolumeProfileOptions(bucketCount: bucketCount);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(bucketCount));
    }

    // ── HvnThresholdRatio validation ─────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]   // нижняя граница — нулевой порог не имеет смысла
    [InlineData(-0.1)]  // отрицательный порог
    [InlineData(-1.0)]  // явно некорректный
    public void Throws_ArgumentOutOfRangeException_When_HvnThresholdRatio_Is_Not_Positive(double ratio)
    {
        var act = () => new VolumeProfileOptions(hvnThresholdRatio: (decimal)ratio);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("hvnThresholdRatio");
    }

    [Theory]
    [InlineData(1.001)]  // чуть выше 1
    [InlineData(2.0)]    // явно выше 1
    public void Throws_ArgumentOutOfRangeException_When_HvnThresholdRatio_Exceeds_One(double ratio)
    {
        var act = () => new VolumeProfileOptions(hvnThresholdRatio: (decimal)ratio);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("hvnThresholdRatio");
    }
}
