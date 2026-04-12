using System.Net;
using System.Net.Http.Json;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

internal static class ProblemDetailsAssertions
{
    public static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedTitle,
        string detailFragment)
    {
        response.StatusCode.Should().Be(expectedStatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        problem.Should().NotBeNull();
        problem.Status.Should().Be((int)expectedStatusCode);
        problem.Title.Should().Be(expectedTitle);
        problem.Detail.Should().Contain(detailFragment);
    }
}


