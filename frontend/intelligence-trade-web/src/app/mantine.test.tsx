import { Button, MantineProvider } from '@mantine/core';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { theme } from '../styles/theme';

describe('Интеграция Mantine', () => {
  it('рендерит типовой компонент с принудительной тёмной темой', () => {
    render(
      <MantineProvider theme={theme} forceColorScheme="dark">
        <Button>Продолжить</Button>
      </MantineProvider>,
    );

    expect(screen.getByRole('button', { name: 'Продолжить' })).toBeVisible();
    expect(document.documentElement).toHaveAttribute('data-mantine-color-scheme', 'dark');
  });
});
