import { describe, expect, it } from 'vitest';
import type { Logger } from '../../src/application/ports.js';
import { logCall } from '../../src/infrastructure/call-logger.js';

const capture = (): { logger: Logger; fields: Record<string, unknown>[] } => {
  const fields: Record<string, unknown>[] = [];
  const at = (_m: string, f?: Record<string, unknown>): void => {
    if (f !== undefined) fields.push(f);
  };
  return { logger: { debug: at, info: at, warn: at, error: at }, fields };
};

describe('logCall extra fields', () => {
  it('can never override the fixed fields', async () => {
    const { logger, fields } = capture();
    await logCall(
      logger,
      'Preview',
      'cid-1',
      {
        refId: 'r1',
        extra: {
          endpoint: 'Spoofed',
          outcome: 'Spoofed',
          correlationId: 'spoofed',
          durationMs: -1,
          refId: 'spoofed',
          eInvoiceStatus: '3',
        },
      },
      () => Promise.resolve(1),
    );
    expect(fields[0]).toMatchObject({
      endpoint: 'Preview',
      outcome: 'Success',
      correlationId: 'cid-1',
      refId: 'r1',
      eInvoiceStatus: '3',
    });
    expect(fields[0]?.['durationMs']).toBeGreaterThanOrEqual(0);
  });

  it('accepts only primitive extra values (type-level)', () => {
    // @ts-expect-error objects are not allowed as extra values
    const ctx: Parameters<typeof logCall>[3] = { extra: { nested: { a: 1 } } };
    expect(ctx).toBeDefined();
  });
});
