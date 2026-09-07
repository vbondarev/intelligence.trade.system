using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Infrastructure;

public static class StartupExtensions
{
    private const string ConnectionStringName = "TradeSystem";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        var credentialProtection = ReadCredentialProtectionOptions(configuration);
        var keyRing = CredentialKeyRing.Create(credentialProtection);

        services.AddDbContext<TradeSystemDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name)));

        services.AddSingleton(keyRing);
        services.AddSingleton<IExchangeCredentialProtector, AesGcmExchangeCredentialProtector>();
        services
            .AddHealthChecks()
            .AddDbContextCheck<TradeSystemDbContext>("postgresql");
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<IExchangeAccountCredentialStore, ExchangeAccountCredentialStore>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPortfolioStateRepository, PortfolioStateRepository>();
        services.AddScoped<IPositionAssessmentRepository, PositionAssessmentRepository>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();

        return services;
    }

    private static CredentialProtectionOptions ReadCredentialProtectionOptions(
        IConfiguration configuration)
    {
        var section = configuration.GetSection(CredentialProtectionOptions.SectionName);
        var keys = section
            .GetSection("Keys")
            .GetChildren()
            .ToDictionary(
                child => child.Key,
                child => child.Value ?? string.Empty,
                StringComparer.Ordinal);

        return new CredentialProtectionOptions
        {
            ActiveKeyId = section["ActiveKeyId"],
            Keys = keys,
        };
    }
}
