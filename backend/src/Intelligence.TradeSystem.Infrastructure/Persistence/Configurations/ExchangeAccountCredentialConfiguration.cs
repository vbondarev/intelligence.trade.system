using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Configurations;

public sealed class ExchangeAccountCredentialConfiguration
    : IEntityTypeConfiguration<ExchangeAccountCredentialEntity>
{
    public void Configure(EntityTypeBuilder<ExchangeAccountCredentialEntity> builder)
    {
        builder.ToTable("exchange_account_credentials", table =>
        {
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_ciphertext_non_empty",
                "octet_length(ciphertext) > 0");
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_ciphertext_max_length",
                $"octet_length(ciphertext) <= {CredentialProtectionLimits.MaximumPayloadBytes}");
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_nonce_length",
                "octet_length(nonce) = 12");
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_authentication_tag_length",
                "octet_length(authentication_tag) = 16");
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_key_id_non_empty",
                "length(encryption_key_id) > 0");
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_format_version_positive",
                "format_version > 0");
            table.HasCheckConstraint(
                "ck_exchange_account_credentials_version_positive",
                "version > 0");
        });

        builder.HasKey(credential => credential.ExchangeAccountId);

        builder.Property(credential => credential.ExchangeAccountId)
            .HasColumnName("exchange_account_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(credential => credential.Ciphertext)
            .HasColumnName("ciphertext")
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(credential => credential.Nonce)
            .HasColumnName("nonce")
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(credential => credential.AuthenticationTag)
            .HasColumnName("authentication_tag")
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(credential => credential.EncryptionKeyId)
            .HasColumnName("encryption_key_id")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(credential => credential.FormatVersion)
            .HasColumnName("format_version")
            .HasColumnType("smallint")
            .IsRequired();
        builder.Property(credential => credential.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(credential => credential.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ExchangeAccountEntity>()
            .WithOne()
            .HasForeignKey<ExchangeAccountCredentialEntity>(
                credential => credential.ExchangeAccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_exchange_account_credentials_exchange_accounts");
    }
}
