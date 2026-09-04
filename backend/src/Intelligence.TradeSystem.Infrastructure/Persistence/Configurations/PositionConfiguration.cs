using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PositionConfiguration : IEntityTypeConfiguration<PositionEntity>
{
    public void Configure(EntityTypeBuilder<PositionEntity> builder)
    {
        builder.ToTable("positions", table => table.HasCheckConstraint(
            "ck_positions_position_idx_non_negative", "position_idx >= 0"));
        builder.HasKey(position => position.Id);

        builder.Property(position => position.Id)
            .HasColumnName("position_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(position => position.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid");
        builder.Property(position => position.InstrumentId)
            .HasColumnName("instrument_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(position => position.PositionSide)
            .HasColumnName("position_side")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(position => position.PositionIdx).HasColumnName("position_idx");
        builder.Property(position => position.MarketCategory)
            .HasColumnName("market_category")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        ConfigureDecimal(builder.Property(position => position.Size).HasColumnName("size").IsRequired());
        ConfigureDecimal(builder.Property(position => position.AverageEntryPrice).HasColumnName("average_entry_price"));
        ConfigureDecimal(builder.Property(position => position.PositionValue).HasColumnName("position_value"));
        ConfigureDecimal(builder.Property(position => position.Leverage).HasColumnName("leverage"));
        ConfigureDecimal(builder.Property(position => position.MarkPrice).HasColumnName("mark_price"));
        ConfigureDecimal(builder.Property(position => position.BreakEvenPrice).HasColumnName("break_even_price"));
        ConfigureDecimal(builder.Property(position => position.LiquidationPrice).HasColumnName("liquidation_price"));
        ConfigureDecimal(builder.Property(position => position.UnrealizedPnl).HasColumnName("unrealized_pnl"));
        ConfigureDecimal(builder.Property(position => position.TakeProfit).HasColumnName("take_profit"));
        ConfigureDecimal(builder.Property(position => position.StopLoss).HasColumnName("stop_loss"));
        ConfigureDecimal(builder.Property(position => position.TrailingStop).HasColumnName("trailing_stop"));

        builder.Property(position => position.FirstDetectedAt)
            .HasColumnName("first_detected_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(position => position.LastObservedAt)
            .HasColumnName("last_observed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(position => position.ClosedAt)
            .HasColumnName("closed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(position => position.TrackingState)
            .HasColumnName("tracking_state")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(position => position.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_positions_exchange_accounts");

        builder.HasIndex(position => new
        {
            position.ExchangeAccountId,
            position.InstrumentId,
            position.PositionSide,
            position.PositionIdx,
        })
        .IsUnique()
        .HasFilter("\"tracking_state\" <> 'Closed'")
        .HasDatabaseName("ux_positions_active_exchange_key");
    }

    private static PropertyBuilder<decimal> ConfigureDecimal(PropertyBuilder<decimal> property) =>
        property.HasColumnType("numeric(38,18)");

    private static PropertyBuilder<decimal?> ConfigureDecimal(PropertyBuilder<decimal?> property) =>
        property.HasColumnType("numeric(38,18)");
}
