using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistRecommendationDecisionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "confidence",
                table: "recommendations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_context_json",
                table: "recommendations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_hash",
                table: "recommendations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "recommendations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confidence",
                table: "recommendations");

            migrationBuilder.DropColumn(
                name: "decision_context_json",
                table: "recommendations");

            migrationBuilder.DropColumn(
                name: "policy_hash",
                table: "recommendations");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "recommendations");
        }
    }
}
