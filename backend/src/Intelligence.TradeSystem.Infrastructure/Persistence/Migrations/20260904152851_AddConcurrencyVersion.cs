using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "recommendations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "positions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "exchange_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recommendations_version_positive",
                table: "recommendations",
                sql: "version > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_positions_version_positive",
                table: "positions",
                sql: "version > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_accounts_version_positive",
                table: "exchange_accounts",
                sql: "version > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recommendations_version_positive",
                table: "recommendations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_positions_version_positive",
                table: "positions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_accounts_version_positive",
                table: "exchange_accounts");

            migrationBuilder.DropColumn(
                name: "version",
                table: "recommendations");

            migrationBuilder.DropColumn(
                name: "version",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "version",
                table: "exchange_accounts");
        }
    }
}
