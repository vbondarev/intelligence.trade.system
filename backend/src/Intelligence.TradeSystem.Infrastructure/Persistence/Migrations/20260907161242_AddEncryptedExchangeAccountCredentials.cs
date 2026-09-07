using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedExchangeAccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_account_credentials",
                columns: table => new
                {
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    authentication_tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    encryption_key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    format_version = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_account_credentials", x => x.exchange_account_id);
                    table.CheckConstraint("ck_exchange_account_credentials_authentication_tag_length", "octet_length(authentication_tag) = 16");
                    table.CheckConstraint("ck_exchange_account_credentials_ciphertext_non_empty", "octet_length(ciphertext) > 0");
                    table.CheckConstraint("ck_exchange_account_credentials_format_version_positive", "format_version > 0");
                    table.CheckConstraint("ck_exchange_account_credentials_key_id_non_empty", "length(encryption_key_id) > 0");
                    table.CheckConstraint("ck_exchange_account_credentials_nonce_length", "octet_length(nonce) = 12");
                    table.CheckConstraint("ck_exchange_account_credentials_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_exchange_account_credentials_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_account_credentials");
        }
    }
}
