using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class RecommendationStabilityStateConfiguration
    : IEntityTypeConfiguration<RecommendationStabilityStateEntity>
{
    public void Configure(EntityTypeBuilder<RecommendationStabilityStateEntity> builder)
    {
        builder.ToTable("recommendation_stability_states", table =>
        {
            table.HasCheckConstraint(
                "ck_recommendation_stability_states_observations_positive",
                "consecutive_observations > 0");
            table.HasCheckConstraint(
                "ck_recommendation_stability_states_observation_order",
                "first_observed_at <= last_observed_at");
            table.HasCheckConstraint(
                "ck_recommendation_stability_states_version_positive",
                "version > 0");
        });
        builder.HasKey(state => state.PositionId);

        builder.Property(state => state.PositionId)
            .HasColumnName("position_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(state => state.StateId)
            .HasColumnName("state_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(state => state.BaselineRecommendationId)
            .HasColumnName("baseline_recommendation_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(state => state.SemanticStateJson)
            .HasColumnName("semantic_state_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(state => state.FirstObservedAt)
            .HasColumnName("first_observed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(state => state.LastObservedAt)
            .HasColumnName("last_observed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(state => state.ConsecutiveObservations)
            .HasColumnName("consecutive_observations")
            .IsRequired();
        builder.Property(state => state.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<PositionEntity>()
            .WithMany()
            .HasForeignKey(state => state.PositionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recommendation_stability_states_positions");
        builder.HasOne<RecommendationEntity>()
            .WithMany()
            .HasPrincipalKey(recommendation => new
            {
                recommendation.Id,
                recommendation.PositionId
            })
            .HasForeignKey(state => new
            {
                state.BaselineRecommendationId,
                state.PositionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recommendation_stability_states_baseline");

        builder.HasIndex(state => new
        {
            state.BaselineRecommendationId,
            state.PositionId
        }).HasDatabaseName("ix_recommendation_stability_states_baseline");
        builder.HasIndex(state => state.StateId)
            .IsUnique()
            .HasDatabaseName("ux_recommendation_stability_states_state_id");
    }
}
