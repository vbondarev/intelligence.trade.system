export function LoadingScreen() {
  return (
    <div className="status-screen" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>Проверяем сессию…</span>
    </div>
  );
}
