using System.Net;
using System.Net.Http.Json;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

internal static class ProblemDetailsAssertions
{
    public static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedTitle,
        string? detailFragment,
        string? expectedCode = null)
    {
        response.StatusCode.Should().Be(expectedStatusCode);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        problem.Should().NotBeNull();
        problem.Type.Should().StartWith("urn:intelligence-trade:error:");
        problem.Status.Should().Be((int)expectedStatusCode);
        problem.Title.Should().Be(expectedTitle);
        problem.Code.Should().NotBeNullOrWhiteSpace();
        problem.TraceId.Should().NotBeNullOrWhiteSpace();

        if (detailFragment is not null)
        {
            problem.Detail.Should().Contain(detailFragment);
        }

        if (expectedCode is not null)
        {
            problem.Code.Should().Be(expectedCode);
        }
    }
}
