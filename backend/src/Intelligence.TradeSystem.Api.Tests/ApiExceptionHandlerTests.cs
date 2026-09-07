using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Application.Concurrency;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task RequestAbortedCancellation_IsHandled_WithoutWritingProblemDetails()
    {
        var problemDetailsService = new Mock<IProblemDetailsService>(MockBehavior.Strict);
        var handler = new ApiExceptionHandler(problemDetailsService.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var httpContext = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token,
        };

        var handled = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        httpContext.Response.HasStarted.Should().BeFalse();
        problemDetailsService.Verify(
            service => service.WriteAsync(It.IsAny<ProblemDetailsContext>()),
            Times.Never);
    }

    [Fact]
    public async Task NonRequestCancellation_Uses_InternalError_Classification()
    {
        var problemDetailsService = new Mock<IProblemDetailsService>(MockBehavior.Strict);
        ProblemDetailsContext? capturedContext = null;
        problemDetailsService
            .Setup(service => service.WriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(context => capturedContext = context)
            .Returns(ValueTask.CompletedTask);
        var handler = new ApiExceptionHandler(problemDetailsService.Object);
        var httpContext = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException("not a request cancellation"),
            CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        capturedContext.Should().NotBeNull();
        capturedContext!.ProblemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
        capturedContext.ProblemDetails.Extensions["code"].Should().Be("internal_error");
        problemDetailsService.Verify(
            service => service.WriteAsync(It.IsAny<ProblemDetailsContext>()),
            Times.Once);
    }

    [Fact]
    public void Diagnostics_Are_Suppressed_For_Expected_Failures()
    {
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new ConcurrencyConflictException("conflict"),
            requestAborted: false).Should().BeTrue();
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new MarketDataUnavailableException("unavailable"),
            requestAborted: false).Should().BeTrue();
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new DataSourceException("invalid upstream data"),
            requestAborted: false).Should().BeTrue();
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new ArgumentException("invalid request"),
            requestAborted: false).Should().BeTrue();
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new NotSupportedException("unsupported request"),
            requestAborted: false).Should().BeTrue();
    }

    [Fact]
    public void Diagnostics_Are_Preserved_For_Unexpected_Failures()
    {
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new InvalidOperationException("unexpected"),
            requestAborted: false).Should().BeFalse();
#pragma warning disable CA2201 // The reserved exception type is intentional for classification coverage.
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new NullReferenceException("unexpected"),
            requestAborted: false).Should().BeFalse();
#pragma warning restore CA2201
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new UnexpectedException(),
            requestAborted: false).Should().BeFalse();
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new OperationCanceledException("not request aborted"),
            requestAborted: false).Should().BeFalse();
        ApiExceptionHandler.ShouldSuppressDiagnostics(
            new OperationCanceledException("request aborted"),
            requestAborted: true).Should().BeTrue();
    }

    private sealed class UnexpectedException : Exception;
}
