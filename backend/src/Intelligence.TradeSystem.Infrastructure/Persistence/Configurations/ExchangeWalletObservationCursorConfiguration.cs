using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class ExchangeWalletObservationCursorConfiguration
    : IEntityTypeConfiguration<ExchangeWalletObservationCursorEntity>
{
    public void Configure(EntityTypeBuilder<ExchangeWalletObservationCursorEntity> builder)
    {
        builder.ToTable("exchange_wallet_observation_cursors");
        builder.HasKey(cursor => cursor.ExchangeAccountId);

        builder.Property(cursor => cursor.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(cursor => cursor.LastMessageCreatedAt)
            .HasColumnName("last_message_created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ExchangeAccountEntity>()
            .WithMany()
            .HasForeignKey(cursor => cursor.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_exchange_wallet_observation_cursors_exchange_accounts");
    }
}
