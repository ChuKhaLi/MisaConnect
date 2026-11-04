import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['test/sandbox/**/*.test.ts'],
    testTimeout: 60_000,
    hookTimeout: 60_000,
    // The sandbox tenant is shared and rate limited: run files/tests serially.
    fileParallelism: false,
  },
});
