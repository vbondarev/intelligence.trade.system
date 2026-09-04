using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class RecommendationReasonConfiguration
    : IEntityTypeConfiguration<RecommendationReasonEntity>
{
    public void Configure(EntityTypeBuilder<RecommendationReasonEntity> builder)
    {
        builder.ToTable("recommendation_reasons");
        builder.HasKey(reason => new { reason.RecommendationId, reason.Sequence });

        builder.Property(reason => reason.RecommendationId)
            .HasColumnName("recommendation_id")
            .HasColumnType("uuid");
        builder.Property(reason => reason.Sequence).HasColumnName("sequence");
        builder.Property(reason => reason.ReasonCode)
            .HasColumnName("reason_code")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.HasOne<RecommendationEntity>()
            .WithMany()
            .HasForeignKey(reason => reason.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_recommendation_reasons_recommendations");
        builder.HasIndex(reason => new { reason.RecommendationId, reason.ReasonCode })
            .IsUnique()
            .HasDatabaseName("ux_recommendation_reasons_code");
    }
}
