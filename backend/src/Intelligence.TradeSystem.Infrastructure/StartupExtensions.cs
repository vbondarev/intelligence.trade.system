using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
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

        services.AddDbContext<TradeSystemDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name)));

        services
            .AddHealthChecks()
            .AddDbContextCheck<TradeSystemDbContext>("postgresql");
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPortfolioStateRepository, PortfolioStateRepository>();
        services.AddScoped<IPositionAssessmentRepository, PositionAssessmentRepository>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();

        return services;
    }
}
