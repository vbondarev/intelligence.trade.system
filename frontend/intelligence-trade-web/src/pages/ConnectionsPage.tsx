import { useCallback, useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/authContext';
import {
  ConnectionsApiError,
  createConnection,
  disconnectConnection,
  listConnections,
  renameConnection,
  restoreConnection,
  rotateCredentials,
  verifyConnection,
} from '../connections/connectionsApi';
import {
  DISPLAY_NAME_MAX_LENGTH,
  type ExchangeAccount,
  type ExchangeAccountCapability,
  type ConnectionErrorCode,
  type ExchangeAccountConnectionStatus,
  type ExchangeCredentials,
} from '../connections/connectionTypes';

const STATUS_LABELS: Record<ExchangeAccountConnectionStatus, string> = {
  unknown: 'Не проверено',
  connected: 'Подключено',
  unavailable: 'Недоступно',
  disabled: 'Отключено',
};

const CAPABILITY_LABELS: Record<ExchangeAccountCapability, string> = {
  readBalance: 'Чтение баланса',
  readPositions: 'Чтение позиций',
};

const ERROR_MESSAGES: Record<ConnectionErrorCode, string> = {
  validation_failed: 'Проверьте заполнение полей.',
  exchange_credentials_invalid: 'Bybit отклонил ключи. Проверьте API key и API secret.',
  exchange_permissions_rejected:
    'Права ключа не подходят: нужен ключ Bybit только на чтение, без торговли и вывода средств.',
  exchange_account_already_exists: 'Этот аккаунт Bybit уже подключён.',
  exchange_account_identity_mismatch:
    'Новые ключи принадлежат другому аккаунту Bybit. Используйте ключи того же аккаунта.',
  exchange_account_disabled: 'Подключение отключено. Сначала восстановите его.',
  exchange_unavailable: 'Bybit сейчас недоступен. Повторите попытку позже.',
  concurrency_conflict: 'Подключение было изменено параллельно. Список обновлён, повторите действие.',
  resource_not_found: 'Подключение не найдено. Список обновлён.',
  authentication_required: 'Сессия завершена. Войдите снова.',
  access_forbidden: 'Недостаточно прав для управления подключениями.',
};

const FALLBACK_ERROR = 'Не удалось выполнить операцию. Повторите попытку позже.';
const SERVICE_UNAVAILABLE = 'Сервис временно недоступен. Повторите попытку позже.';

type PanelMode = 'rename' | 'rotate' | 'restore';

interface Panel {
  id: string;
  mode: PanelMode;
}

interface Notice {
  tone: 'success' | 'error';
  text: string;
  traceId?: string | null;
  sessionEnded?: boolean;
}

function messageForCode(code: string | null): string | undefined {
  return code !== null && Object.hasOwn(ERROR_MESSAGES, code) ? ERROR_MESSAGES[code as ConnectionErrorCode] : undefined;
}

function isSessionEnded(error: unknown): boolean {
  return error instanceof ConnectionsApiError && (error.status === 401 || error.code === 'authentication_required');
}

// UI ветвится только по HTTP status и machine-readable code: title/detail ProblemDetails не показываются.
function toErrorNotice(error: unknown): Notice {
  if (!(error instanceof ConnectionsApiError)) {
    return { tone: 'error', text: FALLBACK_ERROR };
  }

  if (isSessionEnded(error)) {
    return { tone: 'error', text: ERROR_MESSAGES.authentication_required, sessionEnded: true };
  }

  let text = messageForCode(error.code);
  if (text === undefined) {
    if (error.status === 403) {
      text = ERROR_MESSAGES.access_forbidden;
    } else if (error.status === 404) {
      text = ERROR_MESSAGES.resource_not_found;
    } else if (error.status === 400) {
      text = ERROR_MESSAGES.validation_failed;
    } else if (error.status === 503) {
      text = SERVICE_UNAVAILABLE;
    } else {
      text = FALLBACK_ERROR;
    }
  }

  return { tone: 'error', text, traceId: error.traceId };
}

function validateDisplayName(displayName: string): string | null {
  if (displayName.length === 0) {
    return 'Укажите название подключения.';
  }

  return displayName.length > DISPLAY_NAME_MAX_LENGTH
    ? `Название не должно быть длиннее ${DISPLAY_NAME_MAX_LENGTH} символов.`
    : null;
}

/**
 * Читает credentials из формы и сразу очищает поля, чтобы значения не оставались в DOM
 * на время запроса и после его завершения.
 */
function takeCredentials(form: HTMLFormElement): ExchangeCredentials | null {
  const data = new FormData(form);
  const apiKey = String(data.get('apiKey') ?? '');
  const apiSecret = String(data.get('apiSecret') ?? '');
  for (const name of ['apiKey', 'apiSecret']) {
    const field = form.elements.namedItem(name);
    if (field instanceof HTMLInputElement) {
      field.value = '';
    }
  }

  return apiKey.trim().length === 0 || apiSecret.trim().length === 0 ? null : { apiKey, apiSecret };
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('ru-RU', { dateStyle: 'medium', timeStyle: 'short' });
}

export function ConnectionsPage() {
  const { login } = useAuth();
  const [accounts, setAccounts] = useState<ExchangeAccount[] | null>(null);
  const [loadError, setLoadError] = useState<Notice | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [panel, setPanel] = useState<Panel | null>(null);
  const [pending, setPending] = useState(false);

  const listGeneration = useRef(0);

  // Каждое чтение списка делает все предыдущие устаревшими: поздний ответ или ошибка
  // более раннего GET не должны перезаписать результат reload после mutation.
  const loadAccounts = useCallback((signal?: AbortSignal): Promise<void> => {
    const generation = ++listGeneration.current;
    const isCurrent = () => generation === listGeneration.current && signal?.aborted !== true;
    return listConnections(signal).then(
      (items) => {
        if (isCurrent()) {
          setAccounts(items);
          setLoadError(null);
        }
      },
      (error: unknown) => {
        if (isCurrent()) {
          setLoadError(toErrorNotice(error));
        }
      },
    );
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void loadAccounts(controller.signal);
    return () => controller.abort();
  }, [loadAccounts]);

  // REST остаётся source of truth: после любой mutation список перечитывается, а не правится локально.
  const runMutation = useCallback(
    async (action: () => Promise<string>): Promise<boolean> => {
      setPending(true);
      setNotice(null);
      let succeeded = false;
      let sessionEnded = false;
      try {
        setNotice({ tone: 'success', text: await action() });
        setPanel(null);
        succeeded = true;
      } catch (error) {
        sessionEnded = isSessionEnded(error);
        setNotice(toErrorNotice(error));
      }

      if (!sessionEnded) {
        await loadAccounts();
      }

      setPending(false);
      return succeeded;
    },
    [loadAccounts],
  );

  const handleCreate = (displayName: string, credentials: ExchangeCredentials) =>
    runMutation(async () => {
      const result = await createConnection(displayName, credentials);
      return result.outcome === 'created'
        ? `Подключение «${result.account.displayName}» добавлено.`
        : `Этот аккаунт Bybit уже был подключён как «${result.account.displayName}»: подключение восстановлено с прежним названием.`;
    });

  const handleRestore = (account: ExchangeAccount, credentials: ExchangeCredentials) =>
    runMutation(async () => {
      const result = await restoreConnection(account, credentials);
      return result.outcome === 'reconnected'
        ? `Подключение «${result.account.displayName}» восстановлено.`
        : `Ключи принадлежат другому аккаунту Bybit: создано новое подключение «${result.account.displayName}». Подключение «${account.displayName}» осталось отключённым.`;
    });

  const handleRename = (account: ExchangeAccount, displayName: string) =>
    runMutation(async () => {
      const renamed = await renameConnection(account.id, displayName);
      return `Подключение переименовано в «${renamed.displayName}».`;
    });

  const handleVerify = (account: ExchangeAccount) =>
    runMutation(async () => {
      const verified = await verifyConnection(account.id);
      return `Подключение «${verified.displayName}» проверено. Статус: ${STATUS_LABELS[verified.connectionStatus]}.`;
    });

  const handleRotate = (account: ExchangeAccount, credentials: ExchangeCredentials) =>
    runMutation(async () => {
      const rotated = await rotateCredentials(account.id, credentials);
      return `Ключи подключения «${rotated.displayName}» заменены.`;
    });

  const handleDisconnect = (account: ExchangeAccount) => {
    const confirmed = window.confirm(
      `Отключить подключение «${account.displayName}»?\n\n` +
        'Синхронизация с Bybit прекратится. История не удаляется, подключение можно будет восстановить позднее с ключами того же аккаунта.',
    );
    if (!confirmed) {
      return;
    }

    void runMutation(async () => {
      await disconnectConnection(account.id);
      return `Подключение «${account.displayName}» отключено. История сохранена, подключение можно восстановить.`;
    });
  };

  return (
    <section className="page connections-page">
      <h1>Подключения</h1>
      <p className="muted">
        Read-only подключения к Bybit. Ключи используются только для чтения баланса и позиций и не показываются
        повторно.
      </p>

      {notice && <NoticeMessage notice={notice} onLogin={login} />}

      <AddConnectionForm pending={pending} onSubmit={handleCreate} />

      <h2 className="section-title">Мои подключения</h2>
      {loadError && <NoticeMessage notice={loadError} onLogin={login} />}
      {accounts === null && loadError === null && (
        <p className="inline-status" role="status">
          <span className="spinner" aria-hidden="true" />
          Загружаем подключения…
        </p>
      )}
      {accounts !== null && accounts.length === 0 && (
        <p className="muted">Подключений пока нет. Добавьте read-only ключ Bybit.</p>
      )}
      {accounts !== null && accounts.length > 0 && (
        <ul className="connection-list">
          {accounts.map((account) => (
            <li key={account.id}>
              <ConnectionCard
                account={account}
                panel={panel?.id === account.id ? panel.mode : null}
                pending={pending}
                onOpenPanel={(mode) => setPanel({ id: account.id, mode })}
                onClosePanel={() => setPanel(null)}
                onRename={(displayName) => handleRename(account, displayName)}
                onVerify={() => void handleVerify(account)}
                onRotate={(credentials) => handleRotate(account, credentials)}
                onRestore={(credentials) => handleRestore(account, credentials)}
                onDisconnect={() => handleDisconnect(account)}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function NoticeMessage({ notice, onLogin }: { notice: Notice; onLogin: () => void }) {
  if (notice.tone === 'success') {
    return (
      <p className="notice notice-success" role="status">
        {notice.text}
      </p>
    );
  }

  return (
    <div className="alert" role="alert">
      <p className="notice-text">{notice.text}</p>
      {notice.traceId && <p className="notice-trace">Код диагностики: {notice.traceId}</p>}
      {notice.sessionEnded && (
        <button type="button" className="button button-secondary" onClick={onLogin}>
          Войти снова
        </button>
      )}
    </div>
  );
}

function AddConnectionForm({
  pending,
  onSubmit,
}: {
  pending: boolean;
  onSubmit: (displayName: string, credentials: ExchangeCredentials) => Promise<boolean>;
}) {
  const [validation, setValidation] = useState<string | null>(null);
  const idPrefix = useId();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const displayName = String(new FormData(form).get('displayName') ?? '').trim();
    const nameError = validateDisplayName(displayName);
    if (nameError !== null) {
      setValidation(nameError);
      return;
    }

    const credentials = takeCredentials(form);
    if (credentials === null) {
      setValidation('Укажите API key и API secret.');
      return;
    }

    setValidation(null);
    if (await onSubmit(displayName, credentials)) {
      form.reset();
    }
  }

  return (
    <form className="panel connection-form" aria-labelledby={`${idPrefix}-title`} onSubmit={handleSubmit}>
      <h2 id={`${idPrefix}-title`} className="section-title">
        Добавить подключение Bybit
      </h2>
      <div className="field">
        <label htmlFor={`${idPrefix}-name`}>Название *</label>
        <input
          id={`${idPrefix}-name`}
          name="displayName"
          type="text"
          required
          maxLength={DISPLAY_NAME_MAX_LENGTH}
          autoComplete="off"
        />
      </div>
      <CredentialFields idPrefix={idPrefix} />
      {validation && (
        <p className="field-error" role="alert">
          {validation}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button" disabled={pending}>
          Подключить
        </button>
      </div>
    </form>
  );
}

function CredentialFields({ idPrefix, labelPrefix = '' }: { idPrefix: string; labelPrefix?: string }) {
  return (
    <>
      <div className="field">
        <label htmlFor={`${idPrefix}-key`}>{labelPrefix}API key *</label>
        <input
          id={`${idPrefix}-key`}
          name="apiKey"
          type="text"
          required
          autoComplete="off"
          spellCheck={false}
        />
      </div>
      <div className="field">
        <label htmlFor={`${idPrefix}-secret`}>{labelPrefix}API secret *</label>
        <input
          id={`${idPrefix}-secret`}
          name="apiSecret"
          type="password"
          required
          autoComplete="new-password"
          spellCheck={false}
        />
      </div>
    </>
  );
}

interface ConnectionCardProps {
  account: ExchangeAccount;
  panel: PanelMode | null;
  pending: boolean;
  onOpenPanel: (mode: PanelMode) => void;
  onClosePanel: () => void;
  onRename: (displayName: string) => Promise<boolean>;
  onVerify: () => void;
  onRotate: (credentials: ExchangeCredentials) => Promise<boolean>;
  onRestore: (credentials: ExchangeCredentials) => Promise<boolean>;
  onDisconnect: () => void;
}

function ConnectionCard({
  account,
  panel,
  pending,
  onOpenPanel,
  onClosePanel,
  onRename,
  onVerify,
  onRotate,
  onRestore,
  onDisconnect,
}: ConnectionCardProps) {
  const titleId = useId();
  const disabled = account.connectionStatus === 'disabled';

  return (
    <article className={disabled ? 'panel connection-card connection-card-disabled' : 'panel connection-card'} aria-labelledby={titleId}>
      <header className="connection-header">
        <h3 id={titleId} className="connection-title">
          {account.displayName}
        </h3>
        <span className={`connection-status connection-status-${account.connectionStatus}`}>
          {STATUS_LABELS[account.connectionStatus]}
        </span>
      </header>
      <dl className="connection-details">
        <div>
          <dt>Биржа</dt>
          <dd>Bybit</dd>
        </div>
        <div>
          <dt>Доступ</dt>
          <dd>
            {account.capabilities.length === 0
              ? 'Нет подтверждённых прав'
              : account.capabilities.map((capability) => CAPABILITY_LABELS[capability]).join(', ')}
          </dd>
        </div>
        <div>
          <dt>Последняя синхронизация</dt>
          <dd>
            {account.lastSyncedAt === null ? (
              'Синхронизаций ещё не было'
            ) : (
              <time dateTime={account.lastSyncedAt}>{formatTimestamp(account.lastSyncedAt)}</time>
            )}
          </dd>
        </div>
      </dl>

      {panel === null && (
        <div className="connection-actions">
          {!disabled && (
            <button type="button" className="button button-secondary" disabled={pending} onClick={onVerify}>
              Проверить
            </button>
          )}
          <button
            type="button"
            className="button button-secondary"
            disabled={pending}
            onClick={() => onOpenPanel('rename')}
          >
            Переименовать
          </button>
          {disabled ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={pending}
              onClick={() => onOpenPanel('restore')}
            >
              Восстановить
            </button>
          ) : (
            <>
              <button
                type="button"
                className="button button-secondary"
                disabled={pending}
                onClick={() => onOpenPanel('rotate')}
              >
                Заменить ключи
              </button>
              <button type="button" className="button button-secondary" disabled={pending} onClick={onDisconnect}>
                Отключить
              </button>
            </>
          )}
        </div>
      )}

      {panel === 'rename' && (
        <RenameForm account={account} pending={pending} onSubmit={onRename} onCancel={onClosePanel} />
      )}
      {panel === 'rotate' && (
        <CredentialsForm
          title="Новые ключи"
          description="Ключи должны принадлежать тому же аккаунту Bybit. Название не меняется."
          labelPrefix="Новый "
          submitLabel="Заменить ключи"
          pending={pending}
          onSubmit={onRotate}
          onCancel={onClosePanel}
        />
      )}
      {panel === 'restore' && (
        <CredentialsForm
          title="Восстановление подключения"
          description="Укажите read-only ключи того же аккаунта Bybit. Название сохранится."
          submitLabel="Восстановить"
          pending={pending}
          onSubmit={onRestore}
          onCancel={onClosePanel}
        />
      )}
    </article>
  );
}

function RenameForm({
  account,
  pending,
  onSubmit,
  onCancel,
}: {
  account: ExchangeAccount;
  pending: boolean;
  onSubmit: (displayName: string) => Promise<boolean>;
  onCancel: () => void;
}) {
  const [validation, setValidation] = useState<string | null>(null);
  const inputId = useId();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const displayName = String(new FormData(event.currentTarget).get('displayName') ?? '').trim();
    const nameError = validateDisplayName(displayName);
    if (nameError !== null) {
      setValidation(nameError);
      return;
    }

    setValidation(null);
    await onSubmit(displayName);
  }

  return (
    <form className="connection-form" onSubmit={handleSubmit}>
      <div className="field">
        <label htmlFor={inputId}>Новое название *</label>
        <input
          id={inputId}
          name="displayName"
          type="text"
          required
          maxLength={DISPLAY_NAME_MAX_LENGTH}
          defaultValue={account.displayName}
          autoComplete="off"
        />
      </div>
      {validation && (
        <p className="field-error" role="alert">
          {validation}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button" disabled={pending}>
          Сохранить
        </button>
        <button type="button" className="button button-secondary" disabled={pending} onClick={onCancel}>
          Отмена
        </button>
      </div>
    </form>
  );
}

function CredentialsForm({
  title,
  description,
  labelPrefix,
  submitLabel,
  pending,
  onSubmit,
  onCancel,
}: {
  title: string;
  description: string;
  labelPrefix?: string;
  submitLabel: string;
  pending: boolean;
  onSubmit: (credentials: ExchangeCredentials) => Promise<boolean>;
  onCancel: () => void;
}) {
  const [validation, setValidation] = useState<string | null>(null);
  const idPrefix = useId();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const credentials = takeCredentials(event.currentTarget);
    if (credentials === null) {
      setValidation('Укажите API key и API secret.');
      return;
    }

    setValidation(null);
    await onSubmit(credentials);
  }

  return (
    <form className="connection-form" aria-labelledby={`${idPrefix}-title`} onSubmit={handleSubmit}>
      <h4 id={`${idPrefix}-title`} className="form-title">
        {title}
      </h4>
      <p className="muted">{description}</p>
      <CredentialFields idPrefix={idPrefix} labelPrefix={labelPrefix} />
      {validation && (
        <p className="field-error" role="alert">
          {validation}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button" disabled={pending}>
          {submitLabel}
        </button>
        <button type="button" className="button button-secondary" disabled={pending} onClick={onCancel}>
          Отмена
        </button>
      </div>
    </form>
  );
}
