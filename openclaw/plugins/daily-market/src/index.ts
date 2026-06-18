import { Type } from "typebox";
import { defineToolPlugin } from "openclaw/plugin-sdk/tool-plugin";
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";

const DEFAULT_SYMBOL = "BTCUSDT";
const DEFAULT_CHAT_ID = "459142207";
const SCRIPT_PATH = "/home/node/.openclaw/workflows/scripts/run-daily-market-overview-telegram.sh";

function normalizeSymbol(raw: unknown): string {
  const value = typeof raw === "string" ? raw.trim() : "";
  const symbol = value || DEFAULT_SYMBOL;

  if (!/^[A-Z0-9]{3,20}$/.test(symbol)) {
    throw new Error("Invalid symbol. Use format like BTCUSDT.");
  }

  return symbol;
}

export default defineToolPlugin({
  id: "daily-market",
  name: "Daily Market",
  description: "Runs a background daily market overview workflow and sends the result to Telegram.",

  tools: (tool) => [
    tool({
      name: "daily_market",
      label: "Daily Market",
      description: "Start daily market overview workflow in background.",
      parameters: Type.Object({
        command: Type.Optional(Type.String({
          description: "Raw args from /daily slash command, for example BTCUSDT."
        })),
        commandName: Type.Optional(Type.String()),
        skillName: Type.Optional(Type.String())
      }),

      async execute(params) {
        const symbol = normalizeSymbol(params.command);

        if (!existsSync(SCRIPT_PATH)) {
          throw new Error(`Workflow script not found: ${SCRIPT_PATH}`);
        }

        const chatId = process.env.DAILY_MARKET_TELEGRAM_CHAT_ID || DEFAULT_CHAT_ID;

        const child = spawn(SCRIPT_PATH, [symbol, chatId], {
          detached: true,
          stdio: "ignore"
        });

        child.unref();

        return `???????? ?????? ${symbol}. ????????? ?????? ????????? ??????????.`;
      }
    })
  ]
});
