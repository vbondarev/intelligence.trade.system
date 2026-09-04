using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PositionAssessmentConfiguration : IEntityTypeConfiguration<PositionAssessmentEntity>
{
    public void Configure(EntityTypeBuilder<PositionAssessmentEntity> builder)
    {
        builder.ToTable("position_assessments");
        builder.HasKey(assessment => assessment.Id);

        builder.Property(assessment => assessment.Id)
            .HasColumnName("position_assessment_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(assessment => assessment.PositionId)
            .HasColumnName("position_id")
            .HasColumnType("uuid");
        builder.Property(assessment => assessment.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid");
        builder.Property(assessment => assessment.InstrumentId)
            .HasColumnName("instrument_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(assessment => assessment.PositionObservedAt)
            .HasColumnName("position_observed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(assessment => assessment.PortfolioCalculatedAt)
            .HasColumnName("portfolio_calculated_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(assessment => assessment.MarketCapturedAt)
            .HasColumnName("market_captured_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(assessment => assessment.RuleVersion)
            .HasColumnName("rule_version")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(assessment => assessment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(assessment => assessment.ValidUntil)
            .HasColumnName("valid_until")
            .HasColumnType("timestamp with time zone");
        builder.Property(assessment => assessment.PortfolioRiskDecision)
            .HasColumnName("portfolio_risk_decision")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne<PositionEntity>()
            .WithMany()
            .HasForeignKey(assessment => assessment.PositionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_position_assessments_positions");
        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(assessment => assessment.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_position_assessments_exchange_accounts");
    }
}
