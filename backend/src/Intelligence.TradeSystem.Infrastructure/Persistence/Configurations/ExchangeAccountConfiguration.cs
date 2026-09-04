using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class ExchangeAccountConfiguration : IEntityTypeConfiguration<ExchangeAccountEntity>
{
    public void Configure(EntityTypeBuilder<ExchangeAccountEntity> builder)
    {
        builder.ToTable("exchange_accounts");
        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(account => account.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid");
        builder.Property(account => account.ExchangeId)
            .HasColumnName("exchange_id")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(account => account.ConnectionStatus)
            .HasColumnName("connection_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(account => account.Capabilities)
            .HasColumnName("capabilities")
            .HasConversion<int>()
            .HasColumnType("integer")
            .IsRequired();
        builder.Property(account => account.LastSyncedAt)
            .HasColumnName("last_synced_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(account => account.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(2000);
    }
}
