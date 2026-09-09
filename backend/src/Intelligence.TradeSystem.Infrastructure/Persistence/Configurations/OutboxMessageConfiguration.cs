using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("outbox_messages", table =>
        {
            table.HasCheckConstraint(
                "ck_outbox_messages_schema_version_positive",
                "schema_version > 0");
            table.HasCheckConstraint(
                "ck_outbox_messages_attempt_count_non_negative",
                "attempt_count >= 0");
        });
        builder.HasKey(message => message.EventId);

        builder.Property(message => message.EventId)
            .HasColumnName("event_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(message => message.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(message => message.SchemaVersion)
            .HasColumnName("schema_version")
            .IsRequired();
        builder.Property(message => message.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();
        builder.Property(message => message.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(message => message.ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(message => message.ClaimedBy)
            .HasColumnName("claimed_by")
            .HasMaxLength(128);
        builder.Property(message => message.ClaimToken)
            .HasColumnName("claim_token")
            .HasColumnType("uuid");
        builder.Property(message => message.ClaimExpiresAt)
            .HasColumnName("claim_expires_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(message => message.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(message => new
        {
            message.NextAttemptAt,
            message.CreatedAt,
        })
        .HasFilter("\"processed_at\" IS NULL")
        .HasDatabaseName("ix_outbox_messages_pending");

        builder.HasIndex(message => message.ClaimExpiresAt)
            .HasFilter("\"processed_at\" IS NULL")
            .HasDatabaseName("ix_outbox_messages_claim_expiry");
    }
}
