import { createTheme } from '@mantine/core';

// Палитры синхронизированы с tokens.css: в тёмной схеме Mantine берёт фон body из dark[7],
// поверхности и поля ввода — из dark[6], границы — из dark[4], основной текст — из dark[0].
export const theme = createTheme({
  fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
  primaryColor: 'accent',
  primaryShade: 6,
  colors: {
    accent: [
      '#eff6ff',
      '#dbeafe',
      '#bfdbfe',
      '#93c5fd',
      '#60a5fa',
      '#3b82f6',
      '#2563eb',
      '#1d4ed8',
      '#1e40af',
      '#1e3a8a',
    ],
    dark: [
      '#e6edf3',
      '#c3ccd6',
      '#8b98a8',
      '#5c6a7c',
      '#253041',
      '#18202b',
      '#121821',
      '#0b0f14',
      '#080b0f',
      '#05070a',
    ],
  },
});
