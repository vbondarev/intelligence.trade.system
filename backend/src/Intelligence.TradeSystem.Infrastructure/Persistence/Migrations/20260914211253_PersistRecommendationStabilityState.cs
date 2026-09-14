using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistRecommendationStabilityState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_recommendations_position_id",
                table: "recommendations");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_recommendations_id_position",
                table: "recommendations",
                columns: new[] { "recommendation_id", "position_id" });

            migrationBuilder.CreateTable(
                name: "recommendation_stability_states",
                columns: table => new
                {
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    baseline_recommendation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    semantic_state_json = table.Column<string>(type: "jsonb", nullable: false),
                    first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consecutive_observations = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_stability_states", x => x.position_id);
                    table.CheckConstraint("ck_recommendation_stability_states_observation_order", "first_observed_at <= last_observed_at");
                    table.CheckConstraint("ck_recommendation_stability_states_observations_positive", "consecutive_observations > 0");
                    table.CheckConstraint("ck_recommendation_stability_states_version_positive", "version > 0");
                    table.ForeignKey(
                        name: "fk_recommendation_stability_states_baseline",
                        columns: x => new { x.baseline_recommendation_id, x.position_id },
                        principalTable: "recommendations",
                        principalColumns: new[] { "recommendation_id", "position_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recommendation_stability_states_positions",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_recommendations_current_position",
                table: "recommendations",
                column: "position_id",
                unique: true,
                filter: "\"status\" IN ('Active', 'Acknowledged')");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_stability_states_baseline",
                table: "recommendation_stability_states",
                columns: new[] { "baseline_recommendation_id", "position_id" });

            migrationBuilder.Sql(
                """
                ALTER TABLE recommendations
                DROP CONSTRAINT fk_recommendations_successor;
                ALTER TABLE recommendations
                ADD CONSTRAINT fk_recommendations_successor
                FOREIGN KEY (superseded_by_recommendation_id)
                REFERENCES recommendations (recommendation_id)
                ON DELETE RESTRICT
                DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_stability_states");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_recommendations_id_position",
                table: "recommendations");

            migrationBuilder.DropIndex(
                name: "ux_recommendations_current_position",
                table: "recommendations");

            migrationBuilder.Sql(
                """
                ALTER TABLE recommendations
                DROP CONSTRAINT fk_recommendations_successor;
                ALTER TABLE recommendations
                ADD CONSTRAINT fk_recommendations_successor
                FOREIGN KEY (superseded_by_recommendation_id)
                REFERENCES recommendations (recommendation_id)
                ON DELETE RESTRICT
                NOT DEFERRABLE;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_position_id",
                table: "recommendations",
                column: "position_id");
        }
    }
    #pragma warning restore CA1861
}
