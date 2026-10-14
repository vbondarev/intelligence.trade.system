using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PositionObservationCursorConfiguration
    : IEntityTypeConfiguration<PositionObservationCursorEntity>
{
    public void Configure(EntityTypeBuilder<PositionObservationCursorEntity> builder)
    {
        builder.ToTable("position_observation_cursors", table =>
        {
            table.HasCheckConstraint(
                "ck_position_observation_cursors_position_idx_non_negative",
                "position_idx >= 0");
            table.HasCheckConstraint(
                "ck_position_observation_cursors_sequence_positive",
                "last_provider_sequence IS NULL OR last_provider_sequence > 0");
            table.HasCheckConstraint(
                "ck_position_observation_cursors_revision_present",
                "last_provider_sequence IS NOT NULL OR last_provider_updated_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_position_observation_cursors_fingerprint_format",
                "last_accepted_fingerprint ~ '^[A-Za-z0-9:_.-]{1,128}$'");
            table.HasCheckConstraint(
                "ck_position_observation_cursors_version_positive",
                "version > 0");
        });
        builder.HasKey(cursor => new
        {
            cursor.ExchangeAccountId,
            cursor.MarketCategory,
            cursor.InstrumentId,
            cursor.PositionIdx,
        });

        builder.Property(cursor => cursor.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid");
        builder.Property(cursor => cursor.MarketCategory)
            .HasColumnName("market_category")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(cursor => cursor.InstrumentId)
            .HasColumnName("instrument_id")
            .HasMaxLength(PositionObservationCursorKey.SymbolMaxLength)
            .IsRequired();
        builder.Property(cursor => cursor.PositionIdx)
            .HasColumnName("position_idx");
        builder.Property(cursor => cursor.LastProviderSequence)
            .HasColumnName("last_provider_sequence")
            .HasColumnType("bigint");
        builder.Property(cursor => cursor.LastProviderUpdatedAt)
            .HasColumnName("last_provider_updated_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(cursor => cursor.LastMessageCreatedAt)
            .HasColumnName("last_message_created_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(cursor => cursor.LastAcceptedFingerprint)
            .HasColumnName("last_accepted_fingerprint")
            .HasMaxLength(PositionObservationCursor.FingerprintMaxLength)
            .IsRequired();
        builder.Property(cursor => cursor.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(cursor => cursor.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_position_observation_cursors_exchange_accounts");
    }
}
