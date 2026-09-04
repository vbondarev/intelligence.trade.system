#pragma warning disable CA1861

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDomainPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_accounts",
                columns: table => new
                {
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    connection_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    capabilities = table.Column<int>(type: "integer", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_accounts", x => x.exchange_account_id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position_side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position_idx = table.Column<int>(type: "integer", nullable: false),
                    market_category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    size = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    average_entry_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    position_value = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    leverage = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    mark_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    break_even_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    liquidation_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    unrealized_pnl = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    take_profit = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    stop_loss = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    trailing_stop = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    first_detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tracking_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.position_id);
                    table.CheckConstraint("ck_positions_position_idx_non_negative", "position_idx >= 0");
                    table.ForeignKey(
                        name: "fk_positions_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_states",
                columns: table => new
                {
                    portfolio_state_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_equity = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    available_capital = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    capital_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_wallet_balance = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stale_after = table.Column<TimeSpan>(type: "interval", nullable: false),
                    gross_exposure = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    long_exposure = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    short_exposure = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    net_exposure = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    total_unrealized_pnl = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    used_capital = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    free_capital = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    free_capital_percent = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    gross_exposure_to_equity_percent = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    largest_position_concentration_percent = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    largest_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_complete = table.Column<bool>(type: "boolean", nullable: false),
                    is_fresh = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_states", x => x.portfolio_state_id);
                    table.ForeignKey(
                        name: "fk_portfolio_states_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portfolio_states_largest_position",
                        column: x => x.largest_position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_assessments",
                columns: table => new
                {
                    position_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    portfolio_calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    market_captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rule_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    portfolio_risk_decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_assessments", x => x.position_assessment_id);
                    table.ForeignKey(
                        name: "fk_position_assessments_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_assessments_positions",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_changes",
                columns: table => new
                {
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cause = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tracking_state_after = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    before_size = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_average_entry_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_position_value = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_leverage = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_mark_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_break_even_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_liquidation_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_unrealized_pnl = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_take_profit = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_stop_loss = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    before_trailing_stop = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_size = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    after_average_entry_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_position_value = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_leverage = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_mark_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_break_even_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_liquidation_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_unrealized_pnl = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_take_profit = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_stop_loss = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    after_trailing_stop = table.Column<decimal>(type: "numeric(38,18)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_changes", x => new { x.position_id, x.sequence });
                    table.CheckConstraint("ck_position_changes_sequence_positive", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_position_changes_positions",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_position_states",
                columns: table => new
                {
                    portfolio_state_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position_side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position_idx = table.Column<int>(type: "integer", nullable: false),
                    market_category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tracking_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    size = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    position_value = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    unrealized_pnl = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    average_entry_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    mark_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    liquidation_price = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    leverage = table.Column<decimal>(type: "numeric(38,18)", nullable: true),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_position_states", x => new { x.portfolio_state_id, x.sequence });
                    table.CheckConstraint("ck_portfolio_position_states_sequence_positive", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_portfolio_position_states_exchange_accounts",
                        column: x => x.exchange_account_id,
                        principalTable: "exchange_accounts",
                        principalColumn: "exchange_account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portfolio_position_states_portfolio_states",
                        column: x => x.portfolio_state_id,
                        principalTable: "portfolio_states",
                        principalColumn: "portfolio_state_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_portfolio_position_states_positions",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_assessment_reasons",
                columns: table => new
                {
                    position_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_position_assessment_reasons", x => new { x.position_assessment_id, x.sequence });
                    table.ForeignKey(
                        name: "fk_position_assessment_reasons_assessments",
                        column: x => x.position_assessment_id,
                        principalTable: "position_assessments",
                        principalColumn: "position_assessment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommended_action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    add_decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_recommendation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.recommendation_id);
                    table.ForeignKey(
                        name: "fk_recommendations_position_assessments",
                        column: x => x.position_assessment_id,
                        principalTable: "position_assessments",
                        principalColumn: "position_assessment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recommendations_positions",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recommendations_successor",
                        column: x => x.superseded_by_recommendation_id,
                        principalTable: "recommendations",
                        principalColumn: "recommendation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_reasons",
                columns: table => new
                {
                    recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_reasons", x => new { x.recommendation_id, x.sequence });
                    table.ForeignKey(
                        name: "fk_recommendation_reasons_recommendations",
                        column: x => x.recommendation_id,
                        principalTable: "recommendations",
                        principalColumn: "recommendation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_position_states_exchange_account_id",
                table: "portfolio_position_states",
                column: "exchange_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_position_states_position_id",
                table: "portfolio_position_states",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_states_account_calculated_at",
                table: "portfolio_states",
                columns: new[] { "exchange_account_id", "calculated_at", "portfolio_state_id" });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_states_largest_position_id",
                table: "portfolio_states",
                column: "largest_position_id");

            migrationBuilder.CreateIndex(
                name: "ux_position_assessment_reasons_code",
                table: "position_assessment_reasons",
                columns: new[] { "position_assessment_id", "reason_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_position_assessments_exchange_account_id",
                table: "position_assessments",
                column: "exchange_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_position_assessments_position_id",
                table: "position_assessments",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ux_positions_active_exchange_key",
                table: "positions",
                columns: new[] { "exchange_account_id", "instrument_id", "position_side", "position_idx" },
                unique: true,
                filter: "\"tracking_state\" <> 'Closed'");

            migrationBuilder.CreateIndex(
                name: "ux_recommendation_reasons_code",
                table: "recommendation_reasons",
                columns: new[] { "recommendation_id", "reason_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_position_assessment_id",
                table: "recommendations",
                column: "position_assessment_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_position_id",
                table: "recommendations",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_superseded_by_recommendation_id",
                table: "recommendations",
                column: "superseded_by_recommendation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portfolio_position_states");

            migrationBuilder.DropTable(
                name: "position_assessment_reasons");

            migrationBuilder.DropTable(
                name: "position_changes");

            migrationBuilder.DropTable(
                name: "recommendation_reasons");

            migrationBuilder.DropTable(
                name: "portfolio_states");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "position_assessments");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "exchange_accounts");
        }
    }
}
