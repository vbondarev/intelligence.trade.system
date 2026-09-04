using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PositionAssessmentReasonConfiguration: IEntityTypeConfiguration<PositionAssessmentReasonEntity>
{
    public void Configure(EntityTypeBuilder<PositionAssessmentReasonEntity> builder)
    {
        builder.ToTable("position_assessment_reasons");
        builder.HasKey(reason => new { reason.PositionAssessmentId, reason.Sequence });

        builder.Property(reason => reason.PositionAssessmentId)
            .HasColumnName("position_assessment_id")
            .HasColumnType("uuid");
        builder.Property(reason => reason.Sequence).HasColumnName("sequence");
        builder.Property(reason => reason.ReasonCode)
            .HasColumnName("reason_code")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.HasOne<PositionAssessmentEntity>()
            .WithMany()
            .HasForeignKey(reason => reason.PositionAssessmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_position_assessment_reasons_assessments");
        builder.HasIndex(reason => new { reason.PositionAssessmentId, reason.ReasonCode })
            .IsUnique()
            .HasDatabaseName("ux_position_assessment_reasons_code");
    }
}
