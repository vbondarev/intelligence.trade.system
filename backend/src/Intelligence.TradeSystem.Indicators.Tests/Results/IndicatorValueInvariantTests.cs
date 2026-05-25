using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Tests.Results;

/// <summary>
/// Invariant / property-style tests for the <see cref="IndicatorValue"/> contract.
/// These tests guard mathematical and structural constraints that must hold
/// regardless of which calculator produced the value.
/// </summary>
public sealed class IndicatorValueInvariantTests
{
    // ── Invariant: Unavailable always has Value = null ───────────────────────

    public static TheoryData<IndicatorValueReason> NonNoneReasons => new()
    {
        IndicatorValueReason.EmptyInput,
        IndicatorValueReason.InsufficientData,
        IndicatorValueReason.PartialWindow,
        IndicatorValueReason.InvalidInput,
    };

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Unavailable_Always_Has_Null_Value(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Unavailable(reason);

        result.Value.Should().BeNull(
            because: "an unavailable indicator must never carry a numeric value (reason: {0})", reason);
    }

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Unavailable_Always_Has_IsAvailable_False(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Unavailable(reason);

        result.IsAvailable.Should().BeFalse(
            because: "Unavailable(...) must always produce IsAvailable = false (reason: {0})", reason);
    }

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Unavailable_Always_Has_IsFallback_False(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Unavailable(reason);

        result.IsFallback.Should().BeFalse(
            because: "an unavailable result is not a fallback — it has no value at all (reason: {0})", reason);
    }

    // ── Invariant: Fallback always has IsAvailable = true ────────────────────

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Fallback_Always_Has_IsAvailable_True(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Fallback(42m, reason);

        result.IsAvailable.Should().BeTrue(
            because: "a fallback value is usable even if degraded, so IsAvailable must be true (reason: {0})", reason);
    }

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Fallback_Always_Has_IsFallback_True(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Fallback(42m, reason);

        result.IsFallback.Should().BeTrue(
            because: "Fallback(...) must always produce IsFallback = true (reason: {0})", reason);
    }

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Fallback_Always_Has_NonNull_Value(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Fallback(42m, reason);

        result.Value.Should().NotBeNull(
            because: "a fallback result carries a numeric estimate, Value must not be null (reason: {0})", reason);
    }

    // ── Invariant: Reason.None only for Available non-Fallback ───────────────

    [Fact]
    public void Available_Always_Has_Reason_None()
    {
        var result = IndicatorValue.Available(99m);

        result.Reason.Should().Be(IndicatorValueReason.None,
            because: "a fully calculated indicator has no degradation reason");
    }

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Fallback_Never_Has_Reason_None(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Fallback(1m, reason);

        result.Reason.Should().NotBe(IndicatorValueReason.None,
            because: "a fallback result must always carry an explicit reason");
    }

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void Unavailable_Never_Has_Reason_None(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Unavailable(reason);

        result.Reason.Should().NotBe(IndicatorValueReason.None,
            because: "an unavailable result must always carry an explicit reason");
    }

    [Fact]
    public void Fallback_With_Reason_None_Throws()
    {
        var act = () => IndicatorValue.Fallback(1m, IndicatorValueReason.None);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }

    [Fact]
    public void Unavailable_With_Reason_None_Throws()
    {
        var act = () => IndicatorValue.Unavailable(IndicatorValueReason.None);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }

    // ── Invariant: IsAvailable = false implies Value = null ──────────────────

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void When_IsAvailable_Is_False_Value_Is_Always_Null(IndicatorValueReason reason)
    {
        // Единственный путь получить IsAvailable = false — через Unavailable(...).
        var result = IndicatorValue.Unavailable(reason);

        if (!result.IsAvailable)
        {
            result.Value.Should().BeNull(
                because: "the contract guarantees Value is null when IsAvailable is false");
        }
    }

    // ── Invariant: IsFallback = true implies IsAvailable = true ──────────────

    [Theory]
    [MemberData(nameof(NonNoneReasons))]
    public void When_IsFallback_Is_True_IsAvailable_Is_Always_True(IndicatorValueReason reason)
    {
        var result = IndicatorValue.Fallback(0m, reason);

        if (result.IsFallback)
        {
            result.IsAvailable.Should().BeTrue(
                because: "a fallback value is still usable — IsFallback = true implies IsAvailable = true");
        }
    }
}
