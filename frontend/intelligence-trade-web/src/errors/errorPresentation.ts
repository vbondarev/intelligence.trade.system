import { HttpFailure } from './httpFailure';

const messages = {
  validation_failed: [400, 'Проверьте введённые данные.'],
  exchange_credentials_invalid: [400, 'Биржа отклонила API-ключ или секрет.'],
  authentication_required: [401, 'Сессия завершена. Войдите снова.'],
  access_forbidden: [403, 'Недостаточно прав для выполнения операции.'],
  exchange_permissions_rejected: [403, 'Проверьте разрешения API-ключа Bybit.'],
  resource_not_found: [404, 'Запрошенные данные недоступны.'],
  concurrency_conflict: [409, 'Данные изменились во время операции.'],
  position_not_evaluable: [409, 'Позиция сейчас не может быть оценена.'],
  exchange_account_disabled: [409, 'Подключение отключено.'],
  exchange_account_already_exists: [409, 'Это подключение уже существует.'],
  exchange_account_identity_mismatch: [409, 'Ключи принадлежат другому аккаунту Bybit.'],
  market_data_unavailable: [503, 'Рыночные данные временно недоступны. Повторите попытку позже.'],
  exchange_unavailable: [503, 'Bybit сейчас недоступен. Повторите попытку позже.'],
  internal_error: [500, 'Произошёл внутренний сбой.'],
} as const;

type ProblemCode = keyof typeof messages;
type FailureKind = 'validation' | 'session-ended' | 'forbidden' | 'missing'
  | 'conflict' | 'unavailable' | 'internal' | 'invalid-response' | 'unknown';

export interface ErrorPresentation {
  kind: FailureKind;
  text: string;
  traceId: string | null;
  code: ProblemCode | null;
  action: 'login' | 'retry' | null;
}

interface ErrorContext {
  fallback: string;
  retry?: boolean;
  messages?: Partial<Record<ProblemCode | FailureKind, string>>;
  reasons?: Readonly<Record<string, string>>;
}

const defaults: Partial<Record<FailureKind, string>> = {
  'session-ended': messages.authentication_required[1],
  unavailable: 'Сервис временно недоступен. Повторите попытку позже.',
};

function kindOf(error: unknown): FailureKind {
  if (error instanceof TypeError) return 'unavailable';
  if (!(error instanceof HttpFailure)) return 'unknown';
  switch (error.status) {
    case 400: return 'validation';
    case 401: return 'session-ended';
    case 403: return 'forbidden';
    case 404: return 'missing';
    case 409: return 'conflict';
    case 502:
    case 503:
    case 504: return 'unavailable';
    default:
      if (error.status >= 200 && error.status < 300) return 'invalid-response';
      if (error.status >= 500) return 'internal';
      return 'unknown';
  }
}

export function presentError(error: unknown, context: ErrorContext): ErrorPresentation {
  const failure = error instanceof HttpFailure ? error : null;
  const kind = kindOf(error);
  const candidate = failure?.code;
  const code = candidate && Object.hasOwn(messages, candidate) &&
    messages[candidate as ProblemCode][0] === failure.status
    ? candidate as ProblemCode
    : null;
  const reason = code === 'position_not_evaluable' && failure && 'reason' in failure
    ? failure.reason
    : null;
  const reasonText = typeof reason === 'string' && context.reasons && Object.hasOwn(context.reasons, reason)
    ? context.reasons[reason]
    : undefined;
  const text = reasonText ?? (code ? context.messages?.[code] ?? messages[code][1] : undefined)
    ?? context.messages?.[kind] ?? defaults[kind] ?? context.fallback;
  const action = kind === 'session-ended' ? 'login'
    : context.retry && kind !== 'forbidden' && kind !== 'missing' ? 'retry' : null;

  return { kind, text, traceId: failure?.traceId ?? null, code, action };
}
