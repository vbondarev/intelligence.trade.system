using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRestOrderingIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_rest_ordering_intervals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    symbol = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rest_ordering_intervals", x => x.id);
                    table.CheckConstraint("ck_exchange_rest_ordering_intervals_bounds", "started_at <= completed_at");
                    table.CheckConstraint("ck_exchange_rest_ordering_intervals_kind_symbol", "(kind = 'ScopedSymbol' AND symbol IS NOT NULL AND length(btrim(symbol)) > 0)\r\nOR\r\n(kind IN ('FullPositionsComplete', 'Balance') AND symbol IS NULL)");
                    table.ForeignKey(
                        name: "fk_exchange_rest_ordering_intervals_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rest_ordering_intervals_account_kind_bounds",
                table: "exchange_rest_ordering_intervals",
                columns: new[] { "exchange_account_id", "kind", "started_at", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rest_ordering_intervals_account_symbol_bounds",
                table: "exchange_rest_ordering_intervals",
                columns: new[] { "exchange_account_id", "symbol", "started_at", "completed_at" },
                filter: "symbol IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_rest_ordering_intervals");
        }
    }
}
