import { createTheme, Modal } from '@mantine/core';

// Палитры синхронизированы с tokens.css: в тёмной схеме Mantine берёт фон body из dark[7],
// поверхности и поля ввода — из dark[6], контуры элементов — из dark[4], основной текст — из dark[0].
export const theme = createTheme({
  fontFamily: "Geist, 'Segoe UI', system-ui, sans-serif",
  fontSizes: { sm: '.8125rem', md: '.875rem' },
  primaryColor: 'accent',
  primaryShade: 6,
  components: {
    Modal: Modal.extend({
      styles: {
        content: { backgroundColor: 'var(--color-surface)' },
        header: { backgroundColor: 'var(--color-surface)' },
      },
    }),
  },
  colors: {
    accent: [
      '#eff6ff',
      '#dbeafe',
      '#bfdbfe',
      '#92adff',
      '#60a5fa',
      '#3b82f6',
      '#325fdc',
      '#284ec0',
      '#1e40af',
      '#1e3a8a',
    ],
    dark: [
      '#f1f5fc',
      '#c3ccd6',
      '#a2aec3',
      '#8190a1',
      '#667789',
      '#1c2636',
      '#141c28',
      '#0d1118',
      '#080b0f',
      '#05070a',
    ],
  },
});
