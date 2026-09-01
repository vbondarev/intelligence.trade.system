using System.Text.Json;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

internal static class JsonContractAssertions
{
    public static void AssertExactPropertyNames(JsonElement element, params string[] expectedNames)
    {
        element.ValueKind.Should().Be(JsonValueKind.Object);

        element.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(expectedNames);
    }

    public static void AssertValueKind(JsonElement element, params JsonValueKind[] expectedKinds) =>
        expectedKinds.Should().Contain(element.ValueKind);
}
