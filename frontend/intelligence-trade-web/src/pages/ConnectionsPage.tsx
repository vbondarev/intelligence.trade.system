import { presentError } from '../errors/errorPresentation';
import { StatusMessage } from '../ui/StatusMessage';
import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/authContext';
import { ObservationDetails } from '../freshness/FreshnessDetails';
import { explicitPresentation, isStaleSessionAccessFailure, type ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';
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
  exchange_account_already_exists: 'Этот аккаунт Bybit уже подключён. Откройте существующее подключение в списке.',
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

const INPUT_HINTS = {
  displayName: 'Укажите название подключения до 100 символов.',
  apiKey: 'Введите API key.',
  apiSecret: 'Введите API secret.',
};
type InputName = keyof typeof INPUT_HINTS;
type FormErrors = Partial<Record<InputName | 'credentials', string>>;
interface ReportError {
  operation: string;
  handle: (error: unknown) => boolean;
}
const NAME_FIELDS = ['displayName'] as const;
const KEY_FIELDS = ['apiKey', 'apiSecret'] as const;
const CREATE_FIELDS = [...NAME_FIELDS, ...KEY_FIELDS];

function useFormErrors(fields: readonly InputName[], operation: string) {
  const [errors, setErrors] = useState<FormErrors>({});
  const id = useId();
  const pendingFocus = useRef<{ form: HTMLFormElement; field: InputName } | null>(null);

  useLayoutEffect(() => {
    const target = pendingFocus.current;
    pendingFocus.current = null;
    if (!target) return;
    // Браузер должен получить состояние ошибки до события фокуса.
    const frame = requestAnimationFrame(() => {
      if (!target.form.isConnected || !target.form.contains(document.activeElement)) return;
      const input = target.form.elements.namedItem(target.field);
      if (input instanceof HTMLInputElement && input !== document.activeElement) input.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [errors]);

  function show(form: HTMLFormElement, next: FormErrors) {
    if (!form.isConnected) return;
    const field = fields.find((name) => next[name]) ?? (next.credentials ? 'apiKey' : undefined);
    pendingFocus.current = field && form.contains(document.activeElement) ? { form, field } : null;
    setErrors(next);
  }

  function report(form: HTMLFormElement): ReportError {
    function handle(error: unknown) {
      if (!(error instanceof ConnectionsApiError) || error.status !== 400) return false;
      const next: FormErrors = {};
      if (error.code === 'validation_failed') {
        const keys = Object.keys(error.errors ?? {}).map((key) => key.replace(/^\$\./, '').toLowerCase());
        for (const field of fields) {
          if (keys.includes(field.toLowerCase())) next[field] = INPUT_HINTS[field];
        }
      }
      if (error.code === 'exchange_credentials_invalid' && fields.includes('apiKey')) {
        next.credentials = ERROR_MESSAGES.exchange_credentials_invalid;
      }
      if (!Object.keys(next).length || !form.isConnected) return false;
      show(form, next);
      return true;
    }
    return { operation, handle };
  }

  function prepare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const submitter = (event.nativeEvent as SubmitEvent).submitter;
    if (!form.contains(document.activeElement) && submitter instanceof HTMLButtonElement) submitter.focus();
    return form;
  }

  function input(field: InputName) {
    const description = errors[field] ? field : field !== 'displayName' && errors.credentials ? 'credentials' : null;
    return {
      'aria-invalid': description ? true : undefined,
      'aria-describedby': description ? `${id}-${description}` : undefined,
      onChange: () => setErrors((previous) => ({
        ...previous,
        [field]: undefined,
        credentials: field === 'displayName' ? previous.credentials : undefined,
      })),
    };
  }

  function message(field: keyof FormErrors) {
    return errors[field] && <p id={`${id}-${field}`} className="field-error">{errors[field]}</p>;
  }

  return { show, report, prepare, input, message, hasErrors: Object.values(errors).some(Boolean) };
}

function missingCredentials(form: HTMLFormElement): FormErrors {
  const data = new FormData(form);
  const errors: FormErrors = {};
  for (const field of KEY_FIELDS) {
    if (!String(data.get(field) ?? '').trim()) errors[field] = INPUT_HINTS[field];
  }
  return errors;
}

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
  background?: boolean;
  operation?: string;
}

// UI ветвится только по HTTP status и machine-readable code: title/detail ProblemDetails не показываются.
function toErrorNotice(error: unknown): Notice {
  const errorNotice = presentError(error, { fallback: FALLBACK_ERROR, messages: { ...ERROR_MESSAGES, forbidden: ERROR_MESSAGES.access_forbidden, missing: ERROR_MESSAGES.resource_not_found, validation: ERROR_MESSAGES.validation_failed } });
  return { tone: 'error', text: errorNotice.text, traceId: errorNotice.traceId, sessionEnded: errorNotice.action === 'login' };
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
  const { login, captureSessionEnd, captureSessionRead } = useAuth();
  const updates = useAutoUpdates();
  const [accounts, setAccounts] = useState<ExchangeAccount[] | null>(null);
  const [loadError, setLoadError] = useState<Notice | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [panel, setPanel] = useState<Panel | null>(null);
  const [pending, setPending] = useState(false);
  const mutationPending = useRef(false);

  const listGeneration = useRef(0);
  const realtimeRead = useRef<ScreenReader | null>(null);

  // Каждое чтение списка делает все предыдущие устаревшими: поздний ответ или ошибка
  // более раннего GET не должны перезаписать результат reload после mutation.
  const loadAccounts = useCallback((signal?: AbortSignal, view = explicitPresentation): Promise<void> => {
    const session = view === explicitPresentation ? captureSessionRead() : undefined;
    const generation = view.kind === 'silent' ? listGeneration.current : ++listGeneration.current;
    const isCurrent = () => generation === listGeneration.current && signal?.aborted !== true;
    return listConnections(signal).then(
      (items) => view.apply(() => {
        if (isCurrent()) {
          setAccounts(items);
          setLoadError(null);
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent() && !isStaleSessionAccessFailure(error, session)) {
          const message = toErrorNotice(error);
          if (view === explicitPresentation) {
            if (message.sessionEnded) session?.endSession(message.traceId);
            else updates.reportAccessFailure(error);
          }
          view.fail(error, () => {
            if (isCurrent()) setLoadError({ ...message, background: view.kind === 'live' && !message.sessionEnded });
          }, () => {
            listGeneration.current += 1;
            setAccounts(null);
            if (view.kind !== 'silent') setLoadError(message);
          }, isCurrent);
        }
      },
    );
  }, [captureSessionRead, updates]);

  useEffect(() => {
    const scheduler = updates.createReader(loadAccounts);
    realtimeRead.current = scheduler;
    scheduler.refresh();
    return () => { realtimeRead.current = null; scheduler.dispose(); };
  }, [loadAccounts, updates]);

  useRealtimeInvalidation((event) => {
    if (event.type === 'recovery' || event.type === 'exchangeAccount.updated') {
      if (event.type === 'recovery') {
        if (updates.isLive()) listGeneration.current += 1;
        realtimeRead.current?.recover();
      }
      else realtimeRead.current?.invalidate();
    }
  });

  // REST остаётся source of truth: после любой mutation список перечитывается, а не правится локально.
  const runMutation = useCallback(
    async (action: () => Promise<string>, report?: ReportError): Promise<boolean> => {
      if (mutationPending.current) return false;
      const endSession = captureSessionEnd();
      mutationPending.current = true;
      updates.cancelResume();
      setPending(true);
      setNotice(null);
      let succeeded = false;
      let sessionEnded = false;
      try {
        setNotice({ tone: 'success', text: await action() });
        setPanel(null);
        succeeded = true;
      } catch (error) {
        const message = toErrorNotice(error);
        sessionEnded = message.sessionEnded === true;
        if (sessionEnded) endSession(message.traceId);
        if (!report?.handle(error)) setNotice({ ...message, operation: report?.operation });
      }

      try {
        if (!sessionEnded) await loadAccounts();
      } finally {
        mutationPending.current = false;
        setPending(false);
      }

      return succeeded;
    },
    [loadAccounts, updates, captureSessionEnd],
  );

  const handleCreate = (displayName: string, credentials: ExchangeCredentials, report: ReportError) =>
    runMutation(async () => {
      const result = await createConnection(displayName, credentials);
      return result.outcome === 'created'
        ? `Подключение «${result.account.displayName}» добавлено.`
        : `Этот аккаунт Bybit уже был подключён как «${result.account.displayName}»: подключение восстановлено с прежним названием.`;
    }, report);

  const handleRestore = (account: ExchangeAccount, credentials: ExchangeCredentials, report: ReportError) =>
    runMutation(async () => {
      const result = await restoreConnection(account, credentials);
      return result.outcome === 'reconnected'
        ? `Подключение «${result.account.displayName}» восстановлено.`
        : `Ключи принадлежат другому аккаунту Bybit: создано новое подключение «${result.account.displayName}». Подключение «${account.displayName}» осталось отключённым.`;
    }, report);

  const handleRename = (account: ExchangeAccount, displayName: string, report: ReportError) =>
    runMutation(async () => {
      const renamed = await renameConnection(account.id, displayName);
      return `Подключение переименовано в «${renamed.displayName}».`;
    }, report);

  const handleVerify = (account: ExchangeAccount) =>
    runMutation(async () => {
      const verified = await verifyConnection(account.id);
      return `Подключение «${verified.displayName}» проверено. Статус: ${STATUS_LABELS[verified.connectionStatus]}.`;
    });

  const handleRotate = (account: ExchangeAccount, credentials: ExchangeCredentials, report: ReportError) =>
    runMutation(async () => {
      const rotated = await rotateCredentials(account.id, credentials);
      return `Ключи подключения «${rotated.displayName}» заменены.`;
    }, report);

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
      {loadError && (
        <NoticeMessage
          notice={{ ...loadError, background: loadError.background && accounts !== null }}
          onLogin={login}
          onRetry={() => realtimeRead.current?.refresh()}
        />
      )}
      {accounts === null && loadError === null && (
        <StatusMessage kind="loading" text="Загружаем подключения…" />
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
                hasActivePanel={panel !== null}
                pending={pending}
                onOpenPanel={(mode) => setPanel({ id: account.id, mode })}
                onClosePanel={() => setPanel(null)}
                onRename={(displayName, report) => handleRename(account, displayName, report)}
                onVerify={() => void handleVerify(account)}
                onRotate={(credentials, report) => handleRotate(account, credentials, report)}
                onRestore={(credentials, report) => handleRestore(account, credentials, report)}
                onDisconnect={() => handleDisconnect(account)}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function NoticeMessage({ notice, onLogin, onRetry }: { notice: Notice; onLogin: () => void; onRetry?: () => void }) {
  const actions = notice.sessionEnded && (
    <button type="button" className="button button-secondary" onClick={onLogin}>
      Войти снова
    </button>
  ) || (onRetry && <button type="button" className="button button-secondary" onClick={onRetry}>Повторить</button>);
  return <StatusMessage kind={notice.background ? 'warning' : notice.tone} text={notice.text} traceId={notice.traceId} label={notice.operation} actions={actions} />;
}

function AddConnectionForm({
  pending,
  onSubmit,
}: {
  pending: boolean;
  onSubmit: (displayName: string, credentials: ExchangeCredentials, report: ReportError) => Promise<boolean>;
}) {
  const feedback = useFormErrors(CREATE_FIELDS, 'Добавление подключения Bybit');
  const idPrefix = useId();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    const form = feedback.prepare(event);
    if (pending) return;
    const displayName = String(new FormData(form).get('displayName') ?? '').trim();
    const nameError = validateDisplayName(displayName);
    const errors = missingCredentials(form);
    const credentials = takeCredentials(form);
    if (nameError || credentials === null) {
      feedback.show(form, { ...errors, displayName: nameError ?? undefined });
      return;
    }

    feedback.show(form, {});
    if (await onSubmit(displayName, credentials, feedback.report(form))) {
      form.reset();
    }
  }

  return (
    <form className="panel connection-form" aria-labelledby={`${idPrefix}-title`} noValidate onSubmit={handleSubmit}>
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
          {...feedback.input('displayName')}
        />
        {feedback.message('displayName')}
      </div>
      <CredentialFields idPrefix={idPrefix} feedback={feedback} />
      <FormErrorStatus feedback={feedback} />
      <div className="form-actions">
        <button type="submit" className="button" aria-disabled={pending || undefined}>
          Подключить
        </button>
      </div>
    </form>
  );
}

type FormFeedback = ReturnType<typeof useFormErrors>;

function FormErrorStatus({ feedback }: { feedback: FormFeedback }) {
  return <span className="sr-only" aria-live="polite">{feedback.hasErrors ? 'Проверьте поля формы.' : ''}</span>;
}

function CredentialFields({ idPrefix, labelPrefix = '', feedback }: { idPrefix: string; labelPrefix?: string; feedback: FormFeedback }) {
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
          {...feedback.input('apiKey')}
        />
        {feedback.message('apiKey')}
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
          {...feedback.input('apiSecret')}
        />
        {feedback.message('apiSecret')}
      </div>
      {feedback.message('credentials')}
    </>
  );
}

interface ConnectionCardProps {
  account: ExchangeAccount;
  panel: PanelMode | null;
  hasActivePanel: boolean;
  pending: boolean;
  onOpenPanel: (mode: PanelMode) => void;
  onClosePanel: () => void;
  onRename: (displayName: string, report: ReportError) => Promise<boolean>;
  onVerify: () => void;
  onRotate: (credentials: ExchangeCredentials, report: ReportError) => Promise<boolean>;
  onRestore: (credentials: ExchangeCredentials, report: ReportError) => Promise<boolean>;
  onDisconnect: () => void;
}

function ConnectionCard({
  account,
  panel,
  hasActivePanel,
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
  const cardRef = useRef<HTMLElement>(null);
  const openedPanel = useRef<PanelMode | null>(null);

  useEffect(() => {
    if (panel !== null && openedPanel.current !== panel) {
      openedPanel.current = panel;
      cardRef.current?.querySelector<HTMLInputElement>('form input')?.focus();
    } else if (panel === null && hasActivePanel) {
      openedPanel.current = null;
    } else if (panel === null && !pending && openedPanel.current !== null) {
      cardRef.current?.querySelector<HTMLButtonElement>(`[data-panel-action="${openedPanel.current}"]`)?.focus();
      openedPanel.current = null;
    }
  }, [panel, pending, hasActivePanel]);

  return (
    <article ref={cardRef} className={disabled ? 'panel connection-card connection-card-disabled' : 'panel connection-card'} aria-labelledby={titleId}>
      <header className="connection-header">
        <h3 id={titleId} className="connection-title">
          {account.displayName}
        </h3>
        <span className={`connection-status connection-status-${account.connectionStatus}`}>
          {STATUS_LABELS[account.connectionStatus]}
        </span>
      </header>
      <dl className="connection-details">
        <ObservationDetails observation={account.realtimeObservation} />
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
            data-panel-action="rename"
            onClick={() => onOpenPanel('rename')}
          >
            Переименовать
          </button>
          {disabled ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={pending}
              data-panel-action="restore"
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
                data-panel-action="rotate"
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
  onSubmit: (displayName: string, report: ReportError) => Promise<boolean>;
  onCancel: () => void;
}) {
  const feedback = useFormErrors(NAME_FIELDS, 'Переименование подключения');
  const inputId = useId();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    const form = feedback.prepare(event);
    if (pending) return;
    const displayName = String(new FormData(form).get('displayName') ?? '').trim();
    const nameError = validateDisplayName(displayName);
    if (nameError !== null) {
      feedback.show(form, { displayName: nameError });
      return;
    }

    feedback.show(form, {});
    await onSubmit(displayName, feedback.report(form));
  }

  return (
    <form className="connection-form" aria-label="Переименование подключения" noValidate onSubmit={handleSubmit}>
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
          {...feedback.input('displayName')}
        />
        {feedback.message('displayName')}
      </div>
      <FormErrorStatus feedback={feedback} />
      <div className="form-actions">
        <button type="submit" className="button" aria-disabled={pending || undefined}>
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
  onSubmit: (credentials: ExchangeCredentials, report: ReportError) => Promise<boolean>;
  onCancel: () => void;
}) {
  const feedback = useFormErrors(KEY_FIELDS, title);
  const idPrefix = useId();

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    const form = feedback.prepare(event);
    if (pending) return;
    const errors = missingCredentials(form);
    const credentials = takeCredentials(form);
    if (credentials === null) {
      feedback.show(form, errors);
      return;
    }

    feedback.show(form, {});
    await onSubmit(credentials, feedback.report(form));
  }

  return (
    <form className="connection-form" aria-labelledby={`${idPrefix}-title`} noValidate onSubmit={handleSubmit}>
      <h4 id={`${idPrefix}-title`} className="form-title">
        {title}
      </h4>
      <p className="muted">{description}</p>
      <CredentialFields idPrefix={idPrefix} labelPrefix={labelPrefix} feedback={feedback} />
      <FormErrorStatus feedback={feedback} />
      <div className="form-actions">
        <button type="submit" className="button" aria-disabled={pending || undefined}>
          {submitLabel}
        </button>
        <button type="button" className="button button-secondary" disabled={pending} onClick={onCancel}>
          Отмена
        </button>
      </div>
    </form>
  );
}
