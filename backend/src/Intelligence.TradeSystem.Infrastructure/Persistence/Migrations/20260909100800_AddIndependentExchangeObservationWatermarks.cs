using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentExchangeObservationWatermarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_applied_balance_observation_at",
                table: "exchange_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_applied_positions_observation_at",
                table: "exchange_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE exchange_accounts
                SET last_applied_balance_observation_at = last_applied_observation_at,
                    last_applied_positions_observation_at = last_applied_observation_at
                WHERE last_applied_observation_at IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "last_applied_observation_at",
                table: "exchange_accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_applied_observation_at",
                table: "exchange_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE exchange_accounts
                SET last_applied_observation_at =
                    CASE
                        WHEN last_applied_balance_observation_at IS NULL
                            THEN last_applied_positions_observation_at
                        WHEN last_applied_positions_observation_at IS NULL
                            THEN last_applied_balance_observation_at
                        WHEN last_applied_balance_observation_at <= last_applied_positions_observation_at
                            THEN last_applied_balance_observation_at
                        ELSE last_applied_positions_observation_at
                    END;
                """);

            migrationBuilder.DropColumn(
                name: "last_applied_balance_observation_at",
                table: "exchange_accounts");
            migrationBuilder.DropColumn(
                name: "last_applied_positions_observation_at",
                table: "exchange_accounts");
        }
    }
}
