using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeAccountSyncWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_applied_observation_at",
                table: "exchange_accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_applied_observation_at",
                table: "exchange_accounts");
        }
    }
}
