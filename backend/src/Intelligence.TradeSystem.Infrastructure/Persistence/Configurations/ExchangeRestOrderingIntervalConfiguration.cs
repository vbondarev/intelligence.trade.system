using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRestOrderingIntervalConfiguration
    : IEntityTypeConfiguration<ExchangeRestOrderingIntervalEntity>
{
    public void Configure(EntityTypeBuilder<ExchangeRestOrderingIntervalEntity> builder)
    {
        builder.ToTable("exchange_rest_ordering_intervals", table =>
        {
            table.HasCheckConstraint(
                "ck_exchange_rest_ordering_intervals_bounds",
                "started_at <= completed_at");
            table.HasCheckConstraint(
                "ck_exchange_rest_ordering_intervals_kind_symbol",
                """
                (kind = 'ScopedSymbol' AND symbol IS NOT NULL AND length(btrim(symbol)) > 0)
                OR
                (kind IN ('FullPositionsComplete', 'Balance') AND symbol IS NULL)
                """);
        });
        builder.HasKey(interval => interval.Id);

        builder.Property(interval => interval.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(interval => interval.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(interval => interval.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(interval => interval.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(128);
        builder.Property(interval => interval.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(interval => interval.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(interval => interval.RecordedAt)
            .HasColumnName("recorded_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(interval => new { interval.ExchangeAccountId, interval.Kind, interval.StartedAt, interval.CompletedAt })
            .HasDatabaseName("ix_exchange_rest_ordering_intervals_account_kind_bounds");
        builder.HasIndex(interval => new { interval.ExchangeAccountId, interval.Symbol, interval.StartedAt, interval.CompletedAt })
            .HasDatabaseName("ix_exchange_rest_ordering_intervals_account_symbol_bounds")
            .HasFilter("symbol IS NOT NULL");

        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(interval => interval.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_exchange_rest_ordering_intervals_exchange_accounts");
    }
}
