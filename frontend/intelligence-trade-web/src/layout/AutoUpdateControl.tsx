import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useAutoUpdates, useAutoUpdateState } from '../realtime/autoUpdatesContext';

export function AutoUpdateControl({ icon = false }: { icon?: boolean }) {
  const updates = useAutoUpdates();
  const { mode, announcement } = useAutoUpdateState();
  const label = mode === 'live' ? 'Приостановить автообновление' : 'Возобновить автообновление';
  const [tooltipDismissed, setTooltipDismissed] = useState(false);
  const tooltipAnchor = useRef<HTMLDivElement>(null);
  const tooltip = useRef<HTMLSpanElement>(null);

  const positionTooltip = useCallback(() => {
    const anchor = tooltipAnchor.current;
    if (!anchor) return;
    const { left, right } = anchor.getBoundingClientRect();
    const roomOnRight = document.documentElement.clientWidth - right;
    anchor.dataset.tooltipSide = roomOnRight >= left ? 'right' : 'left';
    anchor.style.setProperty('--overview-tooltip-room', `${Math.max(roomOnRight, left) - 8}px`);
  }, []);

  // Статус автообновления и соседние данные могут перенести кнопку при сохранённом фокусе.
  useLayoutEffect(() => {
    if (tooltip.current?.getClientRects().length) positionTooltip();
  });

  useEffect(() => {
    if (!icon) return;
    function dismissTooltip(event: KeyboardEvent) {
      if (event.key === 'Escape' && tooltip.current?.getClientRects().length) {
        setTooltipDismissed(true);
      }
    }
    document.addEventListener('keydown', dismissTooltip);
    window.addEventListener('resize', positionTooltip);
    return () => {
      document.removeEventListener('keydown', dismissTooltip);
      window.removeEventListener('resize', positionTooltip);
    };
  }, [icon, positionTooltip]);

  function activateTooltip() {
    positionTooltip();
    setTooltipDismissed(false);
  }

  const button = (
    <button
      type="button"
      className={icon ? 'button button-secondary overview-icon-button' : 'button button-secondary'}
      aria-disabled={mode === 'resuming' || undefined}
      aria-label={icon ? label : undefined}
      onClick={updates.toggle}
      onFocus={icon ? activateTooltip : undefined}
    >
      {icon ? <span aria-hidden="true">{mode === 'live' ? '⏸' : '▶'}</span> : label}
    </button>
  );

  return (
    <div className={icon ? 'overview-auto-updates' : 'auto-updates'}>
      {icon ? (
        <div className="overview-auto-update-action" ref={tooltipAnchor} onPointerEnter={activateTooltip}>
          {button}
          {!tooltipDismissed && <span className="overview-tooltip" ref={tooltip} role="tooltip">{label}</span>}
        </div>
      ) : button}
      {mode !== 'live' && <p className="muted">Автообновление приостановлено. Данные могут устареть.</p>}
      {mode === 'resuming' && <p>Возобновляем автообновление…</p>}
      <p aria-live="polite" aria-atomic="true" className={mode === 'failed' ? 'alert' : 'screen-reader-only'}>{announcement}</p>
    </div>
  );
}
