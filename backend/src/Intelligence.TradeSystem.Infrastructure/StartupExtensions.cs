using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Infrastructure.ApplicationEvents;
using Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;
using Intelligence.TradeSystem.Infrastructure.Evaluation;
using Intelligence.TradeSystem.Infrastructure.MarketCaching;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.RecommendationPolicy;
using Intelligence.TradeSystem.Infrastructure.Security;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Market.Positions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure;

public static class StartupExtensions
{
    private const string ConnectionStringName = "TradeSystem";

    public static IServiceCollection AddPublicMarketSnapshotCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<
            IValidateOptions<PublicMarketSnapshotCacheOptions>,
            PublicMarketSnapshotCacheOptionsValidator>();
        services
            .AddOptions<PublicMarketSnapshotCacheOptions>()
            .Bind(configuration.GetSection(PublicMarketSnapshotCacheOptions.SectionName))
            .ValidateOnStart();
        services.AddHybridCache();
        services.AddSingleton<IPublicMarketSnapshotCache, PublicMarketSnapshotCache>();
        services.RemoveAll<IMarketSnapshotService>();
        services.AddScoped<IMarketSnapshotService, CachedMarketSnapshotService>();

        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        RegisterRecommendationPolicy(services, configuration, contentRootPath);
        RegisterPositionEvaluationPolicy(services, configuration);

        var connectionString = GetRequiredConnectionString(configuration);

        var credentialProtection = ReadCredentialProtectionOptions(configuration);
        var keyRing = CredentialKeyRing.Create(credentialProtection);

        services.AddDbContext<TradeSystemDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name)));

        services.AddSingleton(keyRing);
        services.AddSingleton<IExchangeCredentialProtector, AesGcmExchangeCredentialProtector>();
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);
        services
            .AddHealthChecks()
            .AddDbContextCheck<TradeSystemDbContext>("postgresql");
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<IExchangeAccountSyncCandidateSource, ExchangeAccountSyncCandidateSource>();
        services.AddScoped<IExchangeAccountCredentialStore, ExchangeAccountCredentialStore>();
        services.AddScoped<IExchangeAccountSyncTransaction, ExchangeAccountSyncTransaction>();
        services.AddScoped<IExchangeAccountLifecycleTransaction, ExchangeAccountLifecycleTransaction>();
        services.AddScoped<IPositionEvaluationTransaction, PositionEvaluationTransaction>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPositionReadStore, PositionReadRepository>();
        services.AddScoped<IPositionTimelineReadStore, PositionTimelineReadRepository>();
        services.AddScoped<IPositionMarketIdentityStore, PositionMarketIdentityRepository>();
        services.AddScoped<PositionReadService>();
        services.AddScoped<PositionTimelineService>();
        services.AddScoped<ApplicationEventOutbox>();
        services.AddScoped<IApplicationEventOutbox>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationEventOutbox>());
        services.AddScoped<IOutboxMessageStore>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationEventOutbox>());
        services.AddScoped<IPortfolioStateRepository, PortfolioStateRepository>();
        services.AddScoped<IPortfolioReadStore, PortfolioReadRepository>();
        services.AddScoped<PortfolioReadService>();
        services.AddScoped<IPositionAssessmentRepository, PositionAssessmentRepository>();
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(IMarketSnapshotService)))
        {
            services.AddScoped<PositionEvaluationService>();
        }
        services.AddScoped<RecommendationRepository>();
        services.AddScoped<IRecommendationRepository>(
            serviceProvider => serviceProvider.GetRequiredService<RecommendationRepository>());
        services.AddScoped<RecommendationStabilityStateRepository>();
        services.AddScoped<IRecommendationStabilityStateRepository>(
            serviceProvider => serviceProvider.GetRequiredService<RecommendationStabilityStateRepository>());
        services.AddScoped<IRecommendationPublicationTransaction, RecommendationPublicationTransaction>();
        services.AddScoped<RecommendationService>();

        return services;
    }

    private static void RegisterPositionEvaluationPolicy(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(PositionEvaluationPolicyOptions.SectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException(
                "PositionEvaluationPolicy configuration is required.");
        }

        var options = section.Get<PositionEvaluationPolicyOptions>()
            ?? throw new InvalidOperationException(
                "PositionEvaluationPolicy configuration is invalid.");
        services.AddSingleton(options.ToDomain());
    }

    private static void RegisterRecommendationPolicy(
        IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath)
    {
        var path = configuration
            .GetSection(RecommendationPolicyOptions.SectionName)["Path"];
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "RecommendationPolicy:Path configuration is required.");

        services.AddSingleton<IRecommendationPolicyDefinitionProvider>(
            new JsonRecommendationPolicyDefinitionProvider(path, contentRootPath));
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

        services.AddSingleton<IExchangeAccountBackgroundSyncSweep, ExchangeAccountBackgroundSyncSweep>();
        services.AddHostedService<ExchangeAccountBackgroundSyncWorker>();
        return services;
    }

    public static IServiceCollection AddApplicationEventOutboxDispatcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddSingleton<IValidateOptions<ApplicationEventOutboxDispatcherOptions>,
                ApplicationEventOutboxDispatcherOptionsValidator>();
        services
            .AddOptions<ApplicationEventOutboxDispatcherOptions>()
            .Bind(configuration.GetSection(ApplicationEventOutboxDispatcherOptions.SectionName))
            .ValidateOnStart();

        services.AddHostedService<ApplicationEventOutboxDispatcherWorker>();
        return services;
    }

    private static string GetRequiredConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } connectionString
        && !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} configuration is required.");

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
