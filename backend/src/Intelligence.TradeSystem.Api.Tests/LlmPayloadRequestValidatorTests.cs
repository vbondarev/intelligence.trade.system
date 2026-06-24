using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Validation;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class LlmPayloadRequestValidatorTests
{
    private readonly LlmPayloadRequestValidator _validator = new();

    [Fact]
    public async Task Exchange_Null_Fails_With_Required_Message()
    {
        var request = new LlmPayloadRequest
        {
            Exchange = null,
            Category = MarketCategory.Linear,
            Mode = AnalysisMode.Intraday,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Field 'exchange' is required.");
    }

    [Fact]
    public async Task Category_Null_Fails_With_Required_Message()
    {
        var request = new LlmPayloadRequest
        {
            Exchange = ExchangeId.Bybit,
            Category = null,
            Mode = AnalysisMode.Intraday,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Field 'category' is required.");
    }

    [Fact]
    public async Task Mode_Null_Is_Valid()
    {
        var request = new LlmPayloadRequest
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Mode = null,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue(because: "null mode means default AnalysisMode.Intraday");
    }

    [Theory]
    [InlineData(AnalysisMode.Swing)] // Valid_Request_With_All_Fields_Passes
    [InlineData(null)]               // Valid_Request_Without_Mode_Passes
    public async Task Valid_Request_Passes(AnalysisMode? mode)
    {
        var request = new LlmPayloadRequest
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Mode = mode,
        };

        var result = await _validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
