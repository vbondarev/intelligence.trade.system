using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861
#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionListReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_positions_list_closed_order",
                table: "positions",
                columns: new[] { "exchange_account_id", "first_detected_at", "position_id" },
                descending: new[] { false, true, true },
                filter: "\"tracking_state\" = 'Closed'");

            migrationBuilder.CreateIndex(
                name: "ix_positions_list_account_order",
                table: "positions",
                columns: new[] { "exchange_account_id", "first_detected_at", "position_id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_positions_list_order",
                table: "positions",
                columns: new[] { "first_detected_at", "position_id", "exchange_account_id" },
                descending: new[] { true, true, false });

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_positions_instrument_lower
                ON positions (lower(instrument_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_positions_list_closed_order",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_list_account_order",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_list_order",
                table: "positions");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_positions_instrument_lower;");
        }
    }
}
