using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionObservationCursorDirectAppliedPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "last_direct_applied_position_id",
                table: "position_observation_cursors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_direct_applied_position_version",
                table: "position_observation_cursors",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_position_observation_cursors_direct_applied_position_pair",
                table: "position_observation_cursors",
                sql: "(last_direct_applied_position_id IS NULL AND last_direct_applied_position_version IS NULL)\r\nOR\r\n(last_direct_applied_position_id IS NOT NULL\r\n AND last_direct_applied_position_version IS NOT NULL\r\n AND last_direct_applied_position_version > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_position_observation_cursors_direct_applied_position_pair",
                table: "position_observation_cursors");

            migrationBuilder.DropColumn(
                name: "last_direct_applied_position_id",
                table: "position_observation_cursors");

            migrationBuilder.DropColumn(
                name: "last_direct_applied_position_version",
                table: "position_observation_cursors");
        }
    }
}
