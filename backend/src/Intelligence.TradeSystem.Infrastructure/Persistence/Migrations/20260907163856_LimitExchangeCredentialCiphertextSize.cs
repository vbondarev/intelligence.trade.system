using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LimitExchangeCredentialCiphertextSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_account_credentials_ciphertext_max_length",
                table: "exchange_account_credentials",
                sql: "octet_length(ciphertext) <= 2097160");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_account_credentials_ciphertext_max_length",
                table: "exchange_account_credentials");
        }
    }
}
