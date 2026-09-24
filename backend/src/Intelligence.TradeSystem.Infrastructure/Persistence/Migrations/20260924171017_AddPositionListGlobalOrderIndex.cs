using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861
#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionListGlobalOrderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_positions_list_order",
                table: "positions",
                columns: new[] { "first_detected_at", "position_id", "exchange_account_id" },
                descending: new[] { true, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_positions_list_order",
                table: "positions");
        }
    }
}
