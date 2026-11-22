import { useAuth } from '../auth/authContext';

export function SessionRecheck({ userId }: { userId: string }) {
  const { revalidate } = useAuth();
  return <button type="button" onClick={() => void revalidate(userId, new AbortController().signal)}>Проверить сессию</button>;
}
