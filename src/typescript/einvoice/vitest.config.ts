import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['test/**/*.test.ts'],
    // Live-sandbox flows are opt-in: `npm run test:sandbox`.
    exclude: ['**/node_modules/**', '**/dist/**', 'test/sandbox/**'],
  },
});
