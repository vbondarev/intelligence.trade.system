import type { ReactNode } from 'react';

type StatusKind = 'error' | 'warning' | 'success' | 'loading' | 'empty';

interface StatusMessageProps {
  kind: StatusKind;
  text: string;
  traceId?: string | null;
  label?: string;
  details?: string | null;
  actions?: ReactNode;
  className?: string;
  announce?: boolean;
}

const classes: Record<StatusKind, string> = {
  error: 'alert',
  warning: 'alert status-warning',
  success: 'notice notice-success',
  loading: 'inline-status',
  empty: 'muted',
};

export function StatusMessage({
  kind, text, traceId, label, details, actions, className = classes[kind], announce = true,
}: StatusMessageProps) {
  const role = !announce || kind === 'empty' ? undefined : kind === 'error' ? 'alert' : 'status';
  return (
    <div className={className} role={role}>
      {kind === 'loading' && <span className="spinner" aria-hidden="true" />}
      {label && <p className="notice-text">{label}</p>}
      <p className="notice-text">{text}</p>
      {details && <p className="notice-text">{details}</p>}
      {traceId && <p className="notice-trace">Код диагностики: {traceId}</p>}
      {actions}
    </div>
  );
}
