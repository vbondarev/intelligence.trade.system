import { StatusMessage } from '../ui/StatusMessage';

export function LoadingScreen() {
  return <StatusMessage kind="loading" className="status-screen" text="Проверяем сессию…" />;
}
