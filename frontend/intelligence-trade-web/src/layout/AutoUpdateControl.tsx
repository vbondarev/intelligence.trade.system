import { useState } from 'react';
import { useAutoUpdates, useAutoUpdateState } from '../realtime/autoUpdatesContext';

export function AutoUpdateControl({ icon = false }: { icon?: boolean }) {
  const updates = useAutoUpdates();
  const { mode, announcement } = useAutoUpdateState();
  const label = mode === 'live' ? 'Приостановить автообновление' : 'Возобновить автообновление';
  const [tooltipDismissed, setTooltipDismissed] = useState(false);

  const button = (
    <button
      type="button"
      className={icon ? 'button button-secondary overview-icon-button' : 'button button-secondary'}
      aria-disabled={mode === 'resuming' || undefined}
      aria-label={icon ? label : undefined}
      onClick={updates.toggle}
      onFocus={icon ? () => setTooltipDismissed(false) : undefined}
      onKeyDown={icon ? (event) => {
        if (event.key === 'Escape') setTooltipDismissed(true);
      } : undefined}
    >
      {icon ? <span aria-hidden="true">{mode === 'live' ? '⏸' : '▶'}</span> : label}
    </button>
  );

  return (
    <div className={icon ? 'overview-auto-updates' : 'auto-updates'}>
      {icon ? (
        <div className="overview-auto-update-action" onPointerEnter={() => setTooltipDismissed(false)}>
          {button}
          {!tooltipDismissed && <span className="overview-tooltip" role="tooltip">{label}</span>}
        </div>
      ) : button}
      {mode !== 'live' && <p className="muted">Автообновление приостановлено. Данные могут устареть.</p>}
      {mode === 'resuming' && <p>Возобновляем автообновление…</p>}
      <p aria-live="polite" aria-atomic="true" className={mode === 'failed' ? 'alert' : 'screen-reader-only'}>{announcement}</p>
    </div>
  );
}
