using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionTimelineReadIndexes : Migration
    {
        private static readonly string[] RecommendationIndexColumns =
            ["position_id", "created_at", "recommendation_id"];
        private static readonly bool[] TimelineIndexDescending = [false, true, true];
        private static readonly string[] PositionChangeIndexColumns =
            ["position_id", "occurred_at", "sequence"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_recommendations_position_created_at_id",
                table: "recommendations",
                columns: RecommendationIndexColumns,
                descending: TimelineIndexDescending);

            migrationBuilder.CreateIndex(
                name: "ix_position_changes_position_occurred_at_sequence",
                table: "position_changes",
                columns: PositionChangeIndexColumns,
                descending: TimelineIndexDescending);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recommendations_position_created_at_id",
                table: "recommendations");

            migrationBuilder.DropIndex(
                name: "ix_position_changes_position_occurred_at_sequence",
                table: "position_changes");
        }
    }
}
