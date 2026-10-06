using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeAccountDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "exchange_accounts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            // Детерминированный fallback только для уже существующих строк: Bybit не опрашивается,
            // provider identity и credentials в имя не попадают.
            migrationBuilder.Sql(
                """
                UPDATE exchange_accounts
                SET display_name = exchange_id || ' ' || left(exchange_account_id::text, 8)
                WHERE display_name IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "exchange_accounts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "exchange_accounts");
        }
    }
}
