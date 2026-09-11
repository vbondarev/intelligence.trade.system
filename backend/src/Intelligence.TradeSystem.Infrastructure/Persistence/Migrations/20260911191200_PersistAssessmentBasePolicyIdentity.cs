using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistAssessmentBasePolicyIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "base_policy_configuration_hash",
                table: "position_assessments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "base_policy_configuration_version",
                table: "position_assessments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "legacy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "base_policy_configuration_hash",
                table: "position_assessments");

            migrationBuilder.DropColumn(
                name: "base_policy_configuration_version",
                table: "position_assessments");
        }
    }
}
