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
            ["Intelligence.TradeSystem.MarketIntelligence"] = ["Intelligence.TradeSystem.Domain"],
            ["Intelligence.TradeSystem.Application"] =
                [
                    "Intelligence.TradeSystem.Domain",
                    "Intelligence.TradeSystem.MarketIntelligence"
                ],
            ["Intelligence.TradeSystem.Exchanges"] = ["Intelligence.TradeSystem.Application", "Intelligence.TradeSystem.Domain"],
            ["Intelligence.TradeSystem.Api"] =
                [
                    "Intelligence.TradeSystem.Application",
                    "Intelligence.TradeSystem.Exchanges",
                    "Intelligence.TradeSystem.Infrastructure",
                    "Intelligence.TradeSystem.ServiceDefaults"
                ],
            ["Intelligence.TradeSystem.Infrastructure"] = ["Intelligence.TradeSystem.Application", "Intelligence.TradeSystem.Domain"],
            ["Intelligence.TradeSystem.Identity"] = ["Intelligence.TradeSystem.ServiceDefaults"],
            ["Intelligence.TradeSystem.Identity.Migrations"] = ["Intelligence.TradeSystem.Identity"],
            ["Intelligence.TradeSystem.ServiceDefaults"] = [],
            ["Intelligence.TradeSystem.Bff"] = ["Intelligence.TradeSystem.ServiceDefaults"],
            ["Intelligence.TradeSystem.AppHost"] =
                [
                    "Intelligence.TradeSystem.Api",
                    "Intelligence.TradeSystem.Identity",
                    "Intelligence.TradeSystem.Identity.Migrations",
                    "Intelligence.TradeSystem.Bff"
                ],
        };

        foreach (var (projectName, expectedReferences) in expectedDependencies)
        {
            var projectPath = Path.Combine(sourceRoot, projectName, $"{projectName}.csproj");
            var actualReferences = GetProjectReferences(projectPath);

            actualReferences.Should().NotContain(reference => reference.EndsWith(".Tests", StringComparison.Ordinal));
            actualReferences.Should().BeEquivalentTo(expectedReferences);
        }
    }

    [Fact]
    public void Bff_Does_Not_Reference_Business_Persistence_Or_Exchange_Projects()
    {
        var sourceRoot = FindSourceRoot();
        var bffReferences = GetProjectReferences(Path.Combine(
            sourceRoot,
            "Intelligence.TradeSystem.Bff",
            "Intelligence.TradeSystem.Bff.csproj"));

        bffReferences.Should().NotContain(
        [
            "Intelligence.TradeSystem.Domain",
            "Intelligence.TradeSystem.MarketIntelligence",
            "Intelligence.TradeSystem.Application",
            "Intelligence.TradeSystem.Infrastructure",
            "Intelligence.TradeSystem.Exchanges",
            "Intelligence.TradeSystem.Api",
            "Intelligence.TradeSystem.Identity"
        ]);
    }

    [Fact]
    public void Frontend_And_Bff_Are_Independent_Build_Units()
    {
        var sourceRoot = FindSourceRoot();
        var frontendRoot = Path.GetFullPath(Path.Combine(sourceRoot, "..", "..", "frontend", "intelligence-trade-web"));
        var bffRoot = Path.Combine(sourceRoot, "Intelligence.TradeSystem.Bff");

        File.Exists(Path.Combine(frontendRoot, "package.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(sourceRoot, "Intelligence.TradeSystem.Web")).Should().BeFalse();
        Directory.Exists(Path.Combine(sourceRoot, "Intelligence.TradeSystem.Web.Tests")).Should().BeFalse();
        Directory.Exists(Path.Combine(bffRoot, "ClientApp")).Should().BeFalse();

        var viteConfig = File.ReadAllText(Path.Combine(frontendRoot, "vite.config.ts"));
        viteConfig.Should().NotContain("wwwroot").And.NotContain("backend");

        // Frontend image собирается из context frontend/intelligence-trade-web и не копирует backend.
        DockerInstructions(Path.Combine(frontendRoot, "Dockerfile"))
            .Should().AllSatisfy(instruction => instruction.Should()
                .NotContainAny("..", "backend", "Intelligence.TradeSystem", "dotnet"));

        // BFF собирается без Node.js/npm и не получает frontend source или dist.
        var bffProject = File.ReadAllText(Path.Combine(bffRoot, "Intelligence.TradeSystem.Bff.csproj"));
        bffProject.Should().NotContainAny("npm", "frontend", "FrontendRoot", "BuildClientAssets", "wwwroot");
        DockerInstructions(Path.Combine(bffRoot, "Dockerfile"))
            .Should().AllSatisfy(instruction => instruction.Should()
                .NotContainAny("..", "node", "npm", "frontend", "wwwroot"));
    }

    private static List<string> DockerInstructions(string dockerfilePath) =>
        File.ReadAllLines(dockerfilePath)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ADD ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("RUN ", StringComparison.OrdinalIgnoreCase))
            .ToList();

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
