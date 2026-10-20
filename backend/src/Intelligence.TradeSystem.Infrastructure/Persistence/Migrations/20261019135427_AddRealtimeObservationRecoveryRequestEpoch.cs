using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeObservationRecoveryRequestEpoch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "recovery_request_epoch",
                table: "exchange_realtime_observations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_realtime_observations_recovery_request_epoch_non_negative",
                table: "exchange_realtime_observations",
                sql: "recovery_request_epoch >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_realtime_observations_recovery_request_epoch_non_negative",
                table: "exchange_realtime_observations");

            migrationBuilder.DropColumn(
                name: "recovery_request_epoch",
                table: "exchange_realtime_observations");
        }
    }
}
