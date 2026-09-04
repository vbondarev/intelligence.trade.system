using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class RecommendationConfiguration : IEntityTypeConfiguration<RecommendationEntity>
{
    public void Configure(EntityTypeBuilder<RecommendationEntity> builder)
    {
        builder.ToTable("recommendations", table => table.HasCheckConstraint(
            "ck_recommendations_version_positive", "version > 0"));
        builder.HasKey(recommendation => recommendation.Id);

        builder.Property(recommendation => recommendation.Id)
            .HasColumnName("recommendation_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(recommendation => recommendation.AssessmentId)
            .HasColumnName("position_assessment_id")
            .HasColumnType("uuid");
        builder.Property(recommendation => recommendation.PositionId)
            .HasColumnName("position_id")
            .HasColumnType("uuid");
        builder.Property(recommendation => recommendation.RecommendedAction)
            .HasColumnName("recommended_action")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(recommendation => recommendation.AddDecision)
            .HasColumnName("add_decision")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(recommendation => recommendation.PolicyVersion)
            .HasColumnName("policy_version")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(recommendation => recommendation.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.ValidUntil)
            .HasColumnName("valid_until")
            .HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(recommendation => recommendation.AcknowledgedAt)
            .HasColumnName("acknowledged_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.DismissedAt)
            .HasColumnName("dismissed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.SupersededAt)
            .HasColumnName("superseded_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.ExpiredAt)
            .HasColumnName("expired_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(recommendation => recommendation.SupersededByRecommendationId)
            .HasColumnName("superseded_by_recommendation_id")
            .HasColumnType("uuid");

        builder.Property(recommendation => recommendation.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<PositionAssessmentEntity>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.AssessmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recommendations_position_assessments");
        builder.HasOne<PositionEntity>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.PositionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recommendations_positions");
        builder.HasOne<RecommendationEntity>()
            .WithMany()
            .HasForeignKey(recommendation => recommendation.SupersededByRecommendationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recommendations_successor");
    }
}
