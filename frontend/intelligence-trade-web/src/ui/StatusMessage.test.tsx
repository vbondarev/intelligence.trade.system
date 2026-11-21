import { render, screen, within } from '@testing-library/react';
import { expect, it } from 'vitest';
import { StatusMessage } from './StatusMessage';

it('различает сообщение об ошибке и фоновое предупреждение', () => {
  const { rerender } = render(<StatusMessage kind="error" text="Запрос не выполнен." traceId="trace" />);
  const alert = screen.getByRole('alert');
  expect(alert).toHaveTextContent('Запрос не выполнен.');
  expect(alert).toHaveTextContent('Код диагностики: trace');

  rerender(<StatusMessage kind="warning" text="Обновление не завершилось." actions={<button>Повторить</button>} />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(within(screen.getByRole('status')).getByRole('button', { name: 'Повторить' })).toBeVisible();
});

it('показывает пустой результат без асинхронного объявления', () => {
  render(<StatusMessage kind="empty" text="Данных пока нет." />);
  expect(screen.getByText('Данных пока нет.')).toBeVisible();
  expect(screen.queryByRole('status')).not.toBeInTheDocument();
});
