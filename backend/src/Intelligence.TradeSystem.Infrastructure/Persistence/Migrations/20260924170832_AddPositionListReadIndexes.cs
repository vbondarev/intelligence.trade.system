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
                name: "ix_positions_list_active_order",
                table: "positions",
                columns: new[] { "exchange_account_id", "tracking_state", "first_detected_at", "position_id" },
                descending: new[] { false, false, true, true },
                filter: "\"tracking_state\" <> 'Closed'");

            migrationBuilder.CreateIndex(
                name: "ix_positions_list_closed_order",
                table: "positions",
                columns: new[] { "exchange_account_id", "first_detected_at", "position_id" },
                descending: new[] { false, true, true },
                filter: "\"tracking_state\" = 'Closed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_positions_list_active_order",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_list_closed_order",
                table: "positions");
        }
    }
}
