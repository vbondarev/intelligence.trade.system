using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PortfolioPositionStateConfiguration : IEntityTypeConfiguration<PortfolioPositionStateEntity>
{
    public void Configure(EntityTypeBuilder<PortfolioPositionStateEntity> builder)
    {
        builder.ToTable("portfolio_position_states", table => table.HasCheckConstraint(
            "ck_portfolio_position_states_sequence_positive", "sequence > 0"));
        builder.HasKey(state => new { state.PortfolioStateId, state.Sequence });

        builder.Property(state => state.PortfolioStateId)
            .HasColumnName("portfolio_state_id")
            .HasColumnType("bigint");
        builder.Property(state => state.Sequence).HasColumnName("sequence");
        builder.Property(state => state.PositionId)
            .HasColumnName("position_id")
            .HasColumnType("uuid");
        builder.Property(state => state.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid");
        builder.Property(state => state.InstrumentId)
            .HasColumnName("instrument_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(state => state.PositionSide)
            .HasColumnName("position_side")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(state => state.PositionIdx).HasColumnName("position_idx");
        builder.Property(state => state.MarketCategory)
            .HasColumnName("market_category")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(state => state.TrackingState)
            .HasColumnName("tracking_state")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        ConfigureDecimal(builder.Property(state => state.Size).HasColumnName("size").IsRequired());
        ConfigureDecimal(builder.Property(state => state.PositionValue).HasColumnName("position_value"));
        ConfigureDecimal(builder.Property(state => state.UnrealizedPnl).HasColumnName("unrealized_pnl"));
        ConfigureDecimal(builder.Property(state => state.AverageEntryPrice).HasColumnName("average_entry_price"));
        ConfigureDecimal(builder.Property(state => state.MarkPrice).HasColumnName("mark_price"));
        ConfigureDecimal(builder.Property(state => state.LiquidationPrice).HasColumnName("liquidation_price"));
        ConfigureDecimal(builder.Property(state => state.Leverage).HasColumnName("leverage"));
        builder.Property(state => state.LastObservedAt)
            .HasColumnName("last_observed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<PositionEntity>()
            .WithMany()
            .HasForeignKey(state => state.PositionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_portfolio_position_states_positions");
        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(state => state.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_portfolio_position_states_exchange_accounts");
    }

    private static PropertyBuilder<decimal> ConfigureDecimal(PropertyBuilder<decimal> property) =>
        property.HasColumnType("numeric(38,18)");

    private static PropertyBuilder<decimal?> ConfigureDecimal(PropertyBuilder<decimal?> property) =>
        property.HasColumnType("numeric(38,18)");
}
