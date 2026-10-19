using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeWalletObservationCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_wallet_observation_cursors",
                columns: table => new
                {
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_wallet_observation_cursors", x => x.exchange_account_id);
                    table.ForeignKey(
                        name: "fk_exchange_wallet_observation_cursors_exchange_accounts",
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
                name: "exchange_wallet_observation_cursors");
        }
    }
}
