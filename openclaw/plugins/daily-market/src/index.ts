import { Type } from "typebox";
import { defineToolPlugin } from "openclaw/plugin-sdk/tool-plugin";
import { spawn } from "node:child_process";
import { closeSync, existsSync, mkdirSync, openSync, unlinkSync } from "node:fs";

const DEFAULT_SYMBOL = "BTCUSDT";
const DEFAULT_MODE = "intraday";
const SCRIPT_PATH = "/home/node/.openclaw/workflows/scripts/run-daily-market-overview-telegram.sh";
const LOCK_ROOT = "/home/node/.openclaw/locks/daily-market";

type SupportedMode = typeof DEFAULT_MODE;

type ParsedCommand = {
  mode: SupportedMode;
  symbol: string;
};

function normalizeSymbol(raw: unknown): string {
  const value = typeof raw === "string" ? raw.trim().toUpperCase() : "";
  const symbol = value || DEFAULT_SYMBOL;

  if (!/^[A-Z0-9]{3,20}$/.test(symbol)) {
    throw new Error("Invalid symbol. Use format like BTCUSDT.");
  }

  return symbol;
}

function parseCommand(raw: unknown): ParsedCommand {
  const value = typeof raw === "string" ? raw.trim() : "";
  const parts = value.split(/\s+/).filter(Boolean);

  if (parts.length === 0) {
    return {
      mode: DEFAULT_MODE,
      symbol: DEFAULT_SYMBOL
    };
  }

  if (parts.length === 1) {
    const first = parts[0].toLowerCase();

    if (first === DEFAULT_MODE) {
      return {
        mode: DEFAULT_MODE,
        symbol: DEFAULT_SYMBOL
      };
    }

    if (first === "swing" || first === "portfolio") {
      throw new Error("Unsupported mode. Current MVP supports only: intraday.");
    }

    return {
      mode: DEFAULT_MODE,
      symbol: normalizeSymbol(parts[0])
    };
  }

  if (parts.length === 2) {
    const mode = parts[0].toLowerCase();
    const symbol = normalizeSymbol(parts[1]);

    if (mode !== DEFAULT_MODE) {
      throw new Error("Unsupported mode. Current MVP supports only: intraday.");
    }

    return {
      mode,
      symbol
    };
  }

  throw new Error("Invalid command. Use format: /crypto BTCUSDT.");
}

function makeRunId(mode: string, symbol: string): string {
  const timestamp = new Date()
    .toISOString()
    .replace(/[-:]/g, "")
    .replace(/\.\d{3}Z$/, "Z");

  return `telegram-${mode}-${symbol}-${timestamp}`;
}

function makeLockPath(mode: string, symbol: string): string {
  return `${LOCK_ROOT}/${mode}-${symbol}.lock`;
}

function acquireLock(lockPath: string): void {
  mkdirSync(LOCK_ROOT, { recursive: true });

  try {
    const fd = openSync(lockPath, "wx");
    closeSync(fd);
  } catch (error: unknown) {
    if (error && typeof error === "object" && "code" in error && error.code === "EEXIST") {
      throw new Error("Market overview is already running for this symbol. Try again later.");
    }

    throw error;
  }
}

function releaseLock(lockPath: string): void {
  try {
    unlinkSync(lockPath);
  } catch {
    // The wrapper also removes the lock on completion. Ignore cleanup races.
  }
}

export default defineToolPlugin({
  id: "daily-market",
  name: "Daily Market",
  description: "Starts a background market-analysis backend workflow. Data source is ONLY market-analysis backend. Includes technical analysis, derivatives, order book and trade flow only. No on-chain, no macro, no news, no visualization, no external sentiment.",

  tools: (tool) => [
    tool({
      name: "daily_market",
      label: "Daily Market",
      description: "Start intraday market overview workflow in background.",
      parameters: Type.Object({
        command: Type.Optional(Type.String({
          description: "Raw args from /crypto or /daily slash command, for example BTCUSDT or intraday BTCUSDT."
        })),
        commandName: Type.Optional(Type.String()),
        skillName: Type.Optional(Type.String())
      }),

      async execute(params) {
        const { mode, symbol } = parseCommand(params.command);

        if (!existsSync(SCRIPT_PATH)) {
          throw new Error(`Workflow script not found: ${SCRIPT_PATH}`);
        }

        const runId = makeRunId(mode, symbol);
        const lockPath = makeLockPath(mode, symbol);

        acquireLock(lockPath);

        try {
          const child = spawn("sh", [SCRIPT_PATH, symbol, runId, lockPath], {
            detached: true,
            stdio: "ignore",
            env: {
              ...process.env,
              ANALYSIS_MODE: mode
            }
          });

          child.once("error", () => releaseLock(lockPath));
          child.unref();
        } catch (error) {
          releaseLock(lockPath);
          throw error;
        }

        return `Запустил интрадей-обзор ${symbol}. Результат придёт в Telegram.`;
      }
    })
  ]
});
