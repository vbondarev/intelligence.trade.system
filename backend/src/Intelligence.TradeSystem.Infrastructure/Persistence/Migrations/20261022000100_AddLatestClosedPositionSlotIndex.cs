using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TradeSystemDbContext))]
[Migration("20261022000100_AddLatestClosedPositionSlotIndex")]
public partial class AddLatestClosedPositionSlotIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Expression index с partial predicate остаётся migration-only, поскольку его нельзя выразить в EF model snapshot.
        migrationBuilder.Sql(
            """
            CREATE INDEX ix_positions_latest_closed_slot
            ON positions (
                exchange_account_id,
                market_category,
                lower(instrument_id),
                position_idx,
                closed_at DESC,
                position_id DESC
            )
            WHERE tracking_state = 'Closed'
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP INDEX ix_positions_latest_closed_slot");
}
