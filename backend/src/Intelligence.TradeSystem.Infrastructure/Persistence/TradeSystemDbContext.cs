using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed class TradeSystemDbContext(DbContextOptions<TradeSystemDbContext> options)
    : DbContext(options)
{
    public DbSet<ExchangeAccountEntity> ExchangeAccounts => Set<ExchangeAccountEntity>();
    public DbSet<PositionEntity> Positions => Set<PositionEntity>();
    public DbSet<PositionChangeEntity> PositionChanges => Set<PositionChangeEntity>();
    public DbSet<PortfolioStateEntity> PortfolioStates => Set<PortfolioStateEntity>();
    public DbSet<PortfolioPositionStateEntity> PortfolioPositionStates => Set<PortfolioPositionStateEntity>();
    public DbSet<PositionAssessmentEntity> PositionAssessments => Set<PositionAssessmentEntity>();
    public DbSet<PositionAssessmentReasonEntity> PositionAssessmentReasons => Set<PositionAssessmentReasonEntity>();
    public DbSet<RecommendationEntity> Recommendations => Set<RecommendationEntity>();
    public DbSet<RecommendationReasonEntity> RecommendationReasons => Set<RecommendationReasonEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradeSystemDbContext).Assembly);
}
