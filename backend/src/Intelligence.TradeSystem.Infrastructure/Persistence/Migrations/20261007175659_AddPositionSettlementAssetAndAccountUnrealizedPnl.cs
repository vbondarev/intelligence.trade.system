using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionSettlementAssetAndAccountUnrealizedPnl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "settlement_asset",
                table: "positions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // До этой migration private sync наблюдал только Linear scopes settleCoin=USDT и
            // settleCoin=USDC, поэтому суффикс символа достоверно определяет актив расчёта
            // существующих Linear позиций. Остальные строки не угадываются: migration
            // останавливается, а не записывает искусственное значение.
            migrationBuilder.Sql(
                """
                UPDATE positions
                SET settlement_asset = CASE
                    WHEN upper(instrument_id) LIKE '%USDT' THEN 'USDT'
                    WHEN upper(instrument_id) LIKE '%USDC' THEN 'USDC'
                END
                WHERE settlement_asset IS NULL
                  AND market_category = 'Linear';
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM positions WHERE settlement_asset IS NULL) THEN
                        RAISE EXCEPTION
                            'Невозможно заполнить positions.settlement_asset: обнаружены позиции вне известных Linear USDT/USDC областей расчёта.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "settlement_asset",
                table: "portfolio_position_states",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE portfolio_position_states AS state
                SET settlement_asset = position.settlement_asset
                FROM positions AS position
                WHERE position.position_id = state.position_id
                  AND state.settlement_asset IS NULL;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM portfolio_position_states WHERE settlement_asset IS NULL) THEN
                        RAISE EXCEPTION
                            'Невозможно заполнить portfolio_position_states.settlement_asset: обнаружены снимки позиций без определённого актива расчёта.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "settlement_asset",
                table: "positions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "settlement_asset",
                table: "portfolio_position_states",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            // Исторические snapshots не получают account-level PnL: сумма position-level PnL
            // разных активов расчёта не является его корректной заменой.
            migrationBuilder.AddColumn<decimal>(
                name: "account_unrealized_pnl",
                table: "portfolio_states",
                type: "numeric(38,18)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "account_unrealized_pnl",
                table: "portfolio_states");

            migrationBuilder.DropColumn(
                name: "settlement_asset",
                table: "portfolio_position_states");

            migrationBuilder.DropColumn(
                name: "settlement_asset",
                table: "positions");
        }
    }
}
