using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRealtimeObservationConfiguration
    : IEntityTypeConfiguration<ExchangeRealtimeObservationEntity>
{
    public void Configure(EntityTypeBuilder<ExchangeRealtimeObservationEntity> builder)
    {
        builder.ToTable("exchange_realtime_observations", table =>
        {
            table.HasCheckConstraint(
                "ck_exchange_realtime_observations_generation_positive",
                "generation > 0");
            table.HasCheckConstraint(
                "ck_exchange_realtime_observations_failure_code_format",
                "failure_code IS NULL OR failure_code ~ '^[A-Za-z][A-Za-z0-9_.-]{0,63}$'");
            table.HasCheckConstraint(
                "ck_exchange_realtime_observations_version_positive",
                "version > 0");
        });
        builder.HasKey(observation => observation.ExchangeAccountId);

        builder.Property(observation => observation.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(observation => observation.Generation)
            .HasColumnName("generation")
            .HasColumnType("bigint")
            .IsRequired();
        builder.Property(observation => observation.TransportStatus)
            .HasColumnName("transport_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(observation => observation.LastConnectedAt)
            .HasColumnName("last_connected_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(observation => observation.LastHeartbeatAt)
            .HasColumnName("last_heartbeat_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(observation => observation.LastFullyReconciledAt)
            .HasColumnName("last_fully_reconciled_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(observation => observation.RecoveryRequired)
            .HasColumnName("recovery_required")
            .IsRequired();
        builder.Property(observation => observation.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(RealtimeObservationState.FailureCodeMaxLength);
        builder.Property(observation => observation.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasOne<ExchangeAccountEntity>()
            .WithOne()
            .HasForeignKey<ExchangeRealtimeObservationEntity>(observation => observation.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_exchange_realtime_observations_exchange_accounts");
    }
}
