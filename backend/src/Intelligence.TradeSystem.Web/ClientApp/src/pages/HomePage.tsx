import { useAuth } from '../auth/authContext';

export function HomePage() {
  const { user } = useAuth();

  return (
    <section className="page">
      <h1>Обзор</h1>
      <p className="muted">Сессия активна{user ? `: ${user.subject}` : ''}.</p>
    </section>
  );
}
