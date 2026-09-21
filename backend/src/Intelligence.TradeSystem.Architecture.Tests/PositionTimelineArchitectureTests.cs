using FluentAssertions;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Application.Users;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class PositionTimelineArchitectureTests
{
    [Fact]
    public void Position_timeline_controller_uses_the_application_read_boundary_only()
    {
        var parameters = typeof(PositionTimelineController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(PositionTimelineService));
        parameters.Should().Contain(typeof(ICurrentUserContext));
        parameters.Should().NotContain(typeof(IPositionTimelineReadStore));
    }
}
