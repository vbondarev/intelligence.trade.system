using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistPositionAssessmentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "policy_configuration_hash",
                table: "position_assessments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "policy_configuration_version",
                table: "position_assessments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "result_json",
                table: "position_assessments",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "policy_configuration_hash",
                table: "position_assessments");

            migrationBuilder.DropColumn(
                name: "policy_configuration_version",
                table: "position_assessments");

            migrationBuilder.DropColumn(
                name: "result_json",
                table: "position_assessments");
        }
    }
}
