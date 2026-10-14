using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class MarketMarkPriceConfiguration : IEntityTypeConfiguration<MarketMarkPriceEntity>
{
    public void Configure(EntityTypeBuilder<MarketMarkPriceEntity> builder)
    {
        builder.ToTable("market_mark_prices", table => table.HasCheckConstraint(
            "ck_market_mark_prices_mark_price_positive",
            "mark_price > 0"));
        builder.HasKey(price => new { price.ExchangeId, price.MarketCategory, price.InstrumentId });

        builder.Property(price => price.ExchangeId)
            .HasColumnName("exchange_id")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(price => price.MarketCategory)
            .HasColumnName("market_category")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(price => price.InstrumentId)
            .HasColumnName("instrument_id")
            .HasMaxLength(MarketMarkPrice.SymbolMaxLength)
            .IsRequired();
        builder.Property(price => price.MarkPrice)
            .HasColumnName("mark_price")
            .HasColumnType("numeric(38,18)")
            .IsRequired();
        builder.Property(price => price.ObservedAt)
            .HasColumnName("observed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(price => price.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(price => price.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
    }
}
