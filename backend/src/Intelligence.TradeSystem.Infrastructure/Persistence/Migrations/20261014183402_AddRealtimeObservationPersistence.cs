using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeObservationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_realtime_observations",
                columns: table => new
                {
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    transport_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_fully_reconciled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recovery_required = table.Column<bool>(type: "boolean", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_realtime_observations", x => x.exchange_account_id);
                    table.CheckConstraint("ck_exchange_realtime_observations_failure_code_format", "failure_code IS NULL OR failure_code ~ '^[A-Za-z][A-Za-z0-9_.-]{0,63}$'");
                    table.CheckConstraint("ck_exchange_realtime_observations_generation_positive", "generation > 0");
                    table.CheckConstraint("ck_exchange_realtime_observations_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_exchange_realtime_observations_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_mark_prices",
                columns: table => new
                {
                    exchange_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    market_category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    instrument_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    mark_price = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_mark_prices", x => new { x.exchange_id, x.market_category, x.instrument_id });
                    table.CheckConstraint("ck_market_mark_prices_mark_price_positive", "mark_price > 0");
                });

            migrationBuilder.CreateTable(
                name: "position_observation_cursors",
                columns: table => new
                {
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    instrument_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position_idx = table.Column<int>(type: "integer", nullable: false),
                    last_provider_sequence = table.Column<long>(type: "bigint", nullable: true),
                    last_provider_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_accepted_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_observation_cursors", x => new { x.exchange_account_id, x.market_category, x.instrument_id, x.position_idx });
                    table.CheckConstraint("ck_position_observation_cursors_fingerprint_format", "last_accepted_fingerprint ~ '^[A-Za-z0-9:_.-]{1,128}$'");
                    table.CheckConstraint("ck_position_observation_cursors_position_idx_non_negative", "position_idx >= 0");
                    table.CheckConstraint("ck_position_observation_cursors_revision_present", "last_provider_sequence IS NOT NULL OR last_provider_updated_at IS NOT NULL");
                    table.CheckConstraint("ck_position_observation_cursors_sequence_positive", "last_provider_sequence IS NULL OR last_provider_sequence > 0");
                    table.CheckConstraint("ck_position_observation_cursors_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_position_observation_cursors_exchange_accounts",
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
                name: "exchange_realtime_observations");

            migrationBuilder.DropTable(
                name: "market_mark_prices");

            migrationBuilder.DropTable(
                name: "position_observation_cursors");
        }
    }
}
