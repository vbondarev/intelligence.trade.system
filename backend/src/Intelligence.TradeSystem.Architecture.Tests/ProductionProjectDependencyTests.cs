using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class ProductionProjectDependencyTests
{
    [Fact]
    public void Production_Projects_Match_The_Current_ProjectReference_Allowlist()
    {
        var sourceRoot = FindSourceRoot();
        var expectedDependencies = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Intelligence.TradeSystem.Domain"] = [],
            ["Intelligence.TradeSystem.Abstractions"] = ["Intelligence.TradeSystem.Domain"],
            ["Intelligence.TradeSystem.MarketIntelligence"] = ["Intelligence.TradeSystem.Domain"],
            ["Intelligence.TradeSystem.Application"] =
                ["Intelligence.TradeSystem.Abstractions", "Intelligence.TradeSystem.Domain", "Intelligence.TradeSystem.MarketIntelligence"],
            ["Intelligence.TradeSystem.Exchanges"] =
                ["Intelligence.TradeSystem.Abstractions", "Intelligence.TradeSystem.Domain"],
            ["Intelligence.TradeSystem.Api"] =
                ["Intelligence.TradeSystem.Application",
                 "Intelligence.TradeSystem.Exchanges", "Intelligence.TradeSystem.ServiceDefaults"],
            ["Intelligence.TradeSystem.ServiceDefaults"] = [],
            ["Intelligence.TradeSystem.AppHost"] = ["Intelligence.TradeSystem.Api"],
        };

        foreach (var (projectName, expectedReferences) in expectedDependencies)
        {
            var projectPath = Path.Combine(sourceRoot, projectName, $"{projectName}.csproj");
            var actualReferences = GetProjectReferences(projectPath);

            actualReferences.Should().NotContain(reference => reference.EndsWith(".Tests", StringComparison.Ordinal));
            actualReferences.Should().BeEquivalentTo(expectedReferences);
        }
    }

    [Theory]
    [InlineData(@"..\Intelligence.TradeSystem.Domain\Intelligence.TradeSystem.Domain.csproj")]
    [InlineData("../Intelligence.TradeSystem.Domain/Intelligence.TradeSystem.Domain.csproj")]
    public void NormalizeProjectReferencePath_Resolves_Windows_And_Unix_Separators(string projectReference)
    {
        var projectDirectory = Path.GetFullPath(
            Path.Combine("repo", "backend", "src", "Intelligence.TradeSystem.Application"));
        var expectedPath = Path.Combine(
            "repo",
            "backend",
            "src",
            "Intelligence.TradeSystem.Domain",
            "Intelligence.TradeSystem.Domain.csproj");

        var normalizedPath = NormalizeProjectReferencePath(projectReference);
        var resolvedPath = Path.GetFullPath(normalizedPath, projectDirectory);

        resolvedPath.Should().Be(Path.GetFullPath(expectedPath));
    }

    private static string[] GetProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var project = XDocument.Load(projectPath);

        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => Path.GetFileNameWithoutExtension(
                Path.GetFullPath(NormalizeProjectReferencePath(reference!), projectDirectory)))
            .ToArray();
    }

    private static string NormalizeProjectReferencePath(string projectReference) =>
        projectReference
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    private static string FindSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Intelligence.TradeSystem.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the backend source root.");
    }
}
