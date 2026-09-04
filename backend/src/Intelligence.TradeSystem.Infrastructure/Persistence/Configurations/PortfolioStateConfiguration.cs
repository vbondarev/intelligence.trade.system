using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class PortfolioStateConfiguration : IEntityTypeConfiguration<PortfolioStateEntity>
{
    public void Configure(EntityTypeBuilder<PortfolioStateEntity> builder)
    {
        builder.ToTable("portfolio_states");
        builder.HasKey(state => state.Id);

        builder.Property(state => state.Id)
            .HasColumnName("portfolio_state_id")
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd()
            .UseIdentityByDefaultColumn();
        builder.Property(state => state.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid");

        ConfigureDecimal(builder.Property(state => state.TotalEquity).HasColumnName("total_equity"));
        ConfigureDecimal(builder.Property(state => state.AvailableCapital).HasColumnName("available_capital"));
        builder.Property(state => state.CapitalObservedAt)
            .HasColumnName("capital_observed_at")
            .HasColumnType("timestamp with time zone");
        ConfigureDecimal(builder.Property(state => state.TotalWalletBalance).HasColumnName("total_wallet_balance"));
        builder.Property(state => state.CalculatedAt)
            .HasColumnName("calculated_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(state => state.StaleAfter)
            .HasColumnName("stale_after")
            .HasColumnType("interval");
        ConfigureDecimal(builder.Property(state => state.GrossExposure).HasColumnName("gross_exposure"));
        ConfigureDecimal(builder.Property(state => state.LongExposure).HasColumnName("long_exposure"));
        ConfigureDecimal(builder.Property(state => state.ShortExposure).HasColumnName("short_exposure"));
        ConfigureDecimal(builder.Property(state => state.NetExposure).HasColumnName("net_exposure"));
        ConfigureDecimal(builder.Property(state => state.TotalUnrealizedPnl).HasColumnName("total_unrealized_pnl"));
        ConfigureDecimal(builder.Property(state => state.UsedCapital).HasColumnName("used_capital"));
        ConfigureDecimal(builder.Property(state => state.FreeCapital).HasColumnName("free_capital"));
        ConfigureDecimal(builder.Property(state => state.FreeCapitalPercent).HasColumnName("free_capital_percent"));
        ConfigureDecimal(builder.Property(state => state.GrossExposureToEquityPercent)
            .HasColumnName("gross_exposure_to_equity_percent"));
        ConfigureDecimal(builder.Property(state => state.LargestPositionConcentrationPercent)
            .HasColumnName("largest_position_concentration_percent"));
        builder.Property(state => state.LargestPositionId)
            .HasColumnName("largest_position_id")
            .HasColumnType("uuid");
        builder.Property(state => state.IsComplete).HasColumnName("is_complete");
        builder.Property(state => state.IsFresh).HasColumnName("is_fresh");

        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(state => state.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_portfolio_states_exchange_accounts");
        builder.HasOne<PositionEntity>()
            .WithMany()
            .HasForeignKey(state => state.LargestPositionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_portfolio_states_largest_position");
        builder.HasMany(state => state.Positions)
            .WithOne()
            .HasForeignKey(state => state.PortfolioStateId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_portfolio_position_states_portfolio_states");

        builder.HasIndex(state => new { state.ExchangeAccountId, state.CalculatedAt, state.Id })
            .HasDatabaseName("ix_portfolio_states_account_calculated_at");
    }

    private static PropertyBuilder<decimal?> ConfigureDecimal(PropertyBuilder<decimal?> property) =>
        property.HasColumnType("numeric(38,18)");
}
