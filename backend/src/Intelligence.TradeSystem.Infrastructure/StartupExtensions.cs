using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IExchangeAccountSyncCandidateSource, ExchangeAccountSyncCandidateSource>();
        services.AddScoped<IExchangeAccountCredentialStore, ExchangeAccountCredentialStore>();
        services.AddScoped<IExchangeAccountSyncTransaction, ExchangeAccountSyncTransaction>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPortfolioStateRepository, PortfolioStateRepository>();
        services.AddScoped<IPositionAssessmentRepository, PositionAssessmentRepository>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();

        return services;
    }

    public static IServiceCollection AddExchangeAccountBackgroundSynchronization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<
            IValidateOptions<ExchangeAccountBackgroundSyncOptions>,
            ExchangeAccountBackgroundSyncOptionsValidator>();
        services
            .AddOptions<ExchangeAccountBackgroundSyncOptions>()
            .Bind(configuration.GetSection(ExchangeAccountBackgroundSyncOptions.SectionName))
            .ValidateOnStart();

        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName)))
        {
            return services;
        }

        services.AddSingleton<IExchangeAccountBackgroundSyncSweep, ExchangeAccountBackgroundSyncSweep>();
        services.AddHostedService<ExchangeAccountBackgroundSyncWorker>();
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
