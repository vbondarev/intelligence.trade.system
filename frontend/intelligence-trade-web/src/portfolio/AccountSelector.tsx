import { useId } from 'react';
import type { ExchangeAccount } from '../connections/connectionTypes';
import { CONNECTION_STATUS_LABELS } from './portfolioFormatting';

/**
 * Выбор подключения для обзора. Показывает все подключения пользователя со статусом,
 * включая отключённые: выбор не скрывает состояние, полученное от backend.
 */
export function AccountSelector({
  accounts,
  selectedId,
  onSelect,
}: {
  accounts: readonly ExchangeAccount[];
  selectedId: string | null;
  onSelect: (id: string) => void;
}) {
  const selectId = useId();

  return (
    <div className="field account-selector">
      <label htmlFor={selectId}>Подключение</label>
      <select id={selectId} value={selectedId ?? ''} onChange={(event) => onSelect(event.target.value)}>
        {selectedId === null && (
          <option value="" disabled>
            Выберите подключение
          </option>
        )}
        {accounts.map((account) => (
          <option key={account.id} value={account.id}>
            {account.displayName} — {CONNECTION_STATUS_LABELS[account.connectionStatus]}
          </option>
        ))}
      </select>
    </div>
  );
}
