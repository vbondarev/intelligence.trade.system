using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionAssessmentLatestIndex : Migration
    {
        private static readonly string[] LatestIndexColumns =
            ["position_id", "created_at", "position_assessment_id"];
        private static readonly bool[] LatestIndexDescending = [false, true, true];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_position_assessments_position_id",
                table: "position_assessments");

            migrationBuilder.CreateIndex(
                name: "ix_position_assessments_position_created_at_id",
                table: "position_assessments",
                columns: LatestIndexColumns,
                descending: LatestIndexDescending);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_position_assessments_position_created_at_id",
                table: "position_assessments");

            migrationBuilder.CreateIndex(
                name: "IX_position_assessments_position_id",
                table: "position_assessments",
                column: "position_id");
        }
    }
}
