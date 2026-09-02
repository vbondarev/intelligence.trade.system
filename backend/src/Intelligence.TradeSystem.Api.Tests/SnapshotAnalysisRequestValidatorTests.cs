using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Validation;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class SnapshotAnalysisRequestValidatorTests
{
    private readonly SnapshotAnalysisRequestValidator _validator = new();

    [Fact]
    public async Task Exchange_Null_Fails_With_Required_Message()
    {
        var request = new SnapshotAnalysisRequest
        {
            Exchange = null,
            Symbol = "BTCUSDT",
            Category = MarketCategory.Linear,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Field 'exchange' is required.");
    }

    [Theory]
    [InlineData(null)]   // Symbol_Null_Fails_With_Required_Message
    [InlineData("")]     // Symbol_Empty_Fails_With_Required_Message
    [InlineData("   ")] // Symbol_Whitespace_Fails_With_Required_Message
    public async Task Symbol_NullOrWhiteSpace_Fails_With_Required_Message(string? symbol)
    {
        var request = new SnapshotAnalysisRequest
        {
            Exchange = ExchangeId.Bybit,
            Symbol = symbol,
            Category = MarketCategory.Linear,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Field 'symbol' is required.");
    }

    [Fact]
    public async Task Category_Null_Fails_With_Required_Message()
    {
        var request = new SnapshotAnalysisRequest
        {
            Exchange = ExchangeId.Bybit,
            Symbol = "BTCUSDT",
            Category = null,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Field 'category' is required.");
    }

    [Theory]
    [InlineData("BTCUSDT")]       // Valid_Request_Passes
    [InlineData("  BTCUSDT  ")]  // Valid_Request_With_Symbol_Spaces_Passes: trim happens in controller
    public async Task Valid_Request_Passes(string symbol)
    {
        var request = new SnapshotAnalysisRequest
        {
            Exchange = ExchangeId.Bybit,
            Symbol = symbol,
            Category = MarketCategory.Linear,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue(because: "validator accepts any non-whitespace symbol; trim is the controller's responsibility");
        result.Errors.Should().BeEmpty();
    }
}
