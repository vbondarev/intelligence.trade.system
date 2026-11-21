export type FieldErrors = Readonly<Record<string, readonly string[]>>;

export interface ProblemFields {
  code: string | null;
  traceId: string | null;
  reason: string | null;
  errors: FieldErrors | null;
}

export class HttpFailure extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code: string | null = null,
    readonly traceId: string | null = null,
  ) {
    super(message);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function own(record: Record<string, unknown>, key: string): unknown {
  return Object.hasOwn(record, key) ? record[key] : undefined;
}

function nonBlank(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function readFieldErrors(value: unknown): FieldErrors | null {
  if (!isRecord(value)) return null;
  const fields: Record<string, readonly string[]> = {};
  for (const [key, messages] of Object.entries(value)) {
    if (key === '__proto__' || key === 'prototype' || key === 'constructor' || !Array.isArray(messages)) continue;
    fields[key] = messages.filter((message): message is string => typeof message === 'string');
  }
  return fields;
}

function rethrowAbort(error: unknown, signal?: AbortSignal | null): void {
  signal?.throwIfAborted();
  if (error instanceof DOMException && error.name === 'AbortError') throw error;
}

export async function decodeProblem(response: Response, signal?: AbortSignal | null): Promise<ProblemFields> {
  let body: unknown;
  const mediaType = response.headers.get('Content-Type')?.split(';')[0]?.trim().toLowerCase();
  if (mediaType === 'application/json' || mediaType?.endsWith('+json')) {
    try {
      body = await response.json();
    } catch (error) {
      rethrowAbort(error, signal);
    }
  }
  signal?.throwIfAborted();
  if (!isRecord(body)) return { code: null, traceId: null, reason: null, errors: null };
  return {
    code: nonBlank(own(body, 'code')),
    traceId: nonBlank(own(body, 'traceId')),
    reason: nonBlank(own(body, 'reason')),
    errors: readFieldErrors(own(body, 'errors')),
  };
}

export async function readSuccessJson(
  response: Response,
  invalid: () => HttpFailure,
  signal?: AbortSignal | null,
): Promise<unknown> {
  signal?.throwIfAborted();
  if (response.bodyUsed || response.body?.locked) throw invalid();
  let body: string;
  try {
    body = await response.text();
  } catch (error) {
    rethrowAbort(error, signal);
    if (error instanceof TypeError) throw error;
    throw invalid();
  }
  signal?.throwIfAborted();
  try {
    return JSON.parse(body) as unknown;
  } catch {
    throw invalid();
  }
}
