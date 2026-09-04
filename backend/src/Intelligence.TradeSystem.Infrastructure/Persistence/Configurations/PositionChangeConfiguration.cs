using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PositionChangeConfiguration : IEntityTypeConfiguration<PositionChangeEntity>
{
    public void Configure(EntityTypeBuilder<PositionChangeEntity> builder)
    {
        builder.ToTable("position_changes", table => table.HasCheckConstraint(
            "ck_position_changes_sequence_positive", "sequence > 0"));
        builder.HasKey(change => new { change.PositionId, change.Sequence });

        builder.Property(change => change.PositionId)
            .HasColumnName("position_id")
            .HasColumnType("uuid");
        builder.Property(change => change.Sequence).HasColumnName("sequence");
        builder.Property(change => change.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(change => change.Cause)
            .HasColumnName("cause")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(change => change.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(change => change.TrackingStateAfter)
            .HasColumnName("tracking_state_after")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        ConfigureDecimal(builder.Property(change => change.BeforeSize).HasColumnName("before_size"));
        ConfigureDecimal(builder.Property(change => change.BeforeAverageEntryPrice).HasColumnName("before_average_entry_price"));
        ConfigureDecimal(builder.Property(change => change.BeforePositionValue).HasColumnName("before_position_value"));
        ConfigureDecimal(builder.Property(change => change.BeforeLeverage).HasColumnName("before_leverage"));
        ConfigureDecimal(builder.Property(change => change.BeforeMarkPrice).HasColumnName("before_mark_price"));
        ConfigureDecimal(builder.Property(change => change.BeforeBreakEvenPrice).HasColumnName("before_break_even_price"));
        ConfigureDecimal(builder.Property(change => change.BeforeLiquidationPrice).HasColumnName("before_liquidation_price"));
        ConfigureDecimal(builder.Property(change => change.BeforeUnrealizedPnl).HasColumnName("before_unrealized_pnl"));
        ConfigureDecimal(builder.Property(change => change.BeforeTakeProfit).HasColumnName("before_take_profit"));
        ConfigureDecimal(builder.Property(change => change.BeforeStopLoss).HasColumnName("before_stop_loss"));
        ConfigureDecimal(builder.Property(change => change.BeforeTrailingStop).HasColumnName("before_trailing_stop"));

        ConfigureDecimal(builder.Property(change => change.AfterSize).HasColumnName("after_size").IsRequired());
        ConfigureDecimal(builder.Property(change => change.AfterAverageEntryPrice).HasColumnName("after_average_entry_price"));
        ConfigureDecimal(builder.Property(change => change.AfterPositionValue).HasColumnName("after_position_value"));
        ConfigureDecimal(builder.Property(change => change.AfterLeverage).HasColumnName("after_leverage"));
        ConfigureDecimal(builder.Property(change => change.AfterMarkPrice).HasColumnName("after_mark_price"));
        ConfigureDecimal(builder.Property(change => change.AfterBreakEvenPrice).HasColumnName("after_break_even_price"));
        ConfigureDecimal(builder.Property(change => change.AfterLiquidationPrice).HasColumnName("after_liquidation_price"));
        ConfigureDecimal(builder.Property(change => change.AfterUnrealizedPnl).HasColumnName("after_unrealized_pnl"));
        ConfigureDecimal(builder.Property(change => change.AfterTakeProfit).HasColumnName("after_take_profit"));
        ConfigureDecimal(builder.Property(change => change.AfterStopLoss).HasColumnName("after_stop_loss"));
        ConfigureDecimal(builder.Property(change => change.AfterTrailingStop).HasColumnName("after_trailing_stop"));

        builder.HasOne<PositionEntity>()
            .WithMany()
            .HasForeignKey(change => change.PositionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_position_changes_positions");
    }

    private static PropertyBuilder<decimal?> ConfigureDecimal(PropertyBuilder<decimal?> property) =>
        property.HasColumnType("numeric(38,18)");

    private static PropertyBuilder<decimal> ConfigureDecimal(PropertyBuilder<decimal> property) =>
        property.HasColumnType("numeric(38,18)");
}
