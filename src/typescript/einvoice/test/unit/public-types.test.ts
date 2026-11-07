import { describe, expect, it } from 'vitest';
import type {
  AccessToken,
  Clock,
  InvoiceValidator,
  RefIdGenerator,
  TemplateResolution,
  TemplateResolver,
  TokenCache,
} from '@misaconnect/einvoice';
import { defaultInvoiceValidator, resolveTemplate } from '@misaconnect/einvoice';

// Type-level check: the swappable collaborators advertised in the README are implementable
// from the public entry point alone (`pnpm run typecheck` fails if a port type is not exported).
describe('public collaborator types', () => {
  it('can implement every port from the package entry point', async () => {
    const store = new Map<string, string>();
    const tokenCache: TokenCache = {
      get: (key) => {
        const raw = store.get(key);
        if (raw === undefined) return Promise.resolve(undefined);
        const parsed = JSON.parse(raw) as { value: string; expiresAt: string };
        const token: AccessToken = { value: parsed.value, expiresAt: new Date(parsed.expiresAt) };
        return Promise.resolve(token);
      },
      set: (key, token) => {
        store.set(key, JSON.stringify(token));
        return Promise.resolve();
      },
      delete: (key) => {
        store.delete(key);
        return Promise.resolve();
      },
    };
    const clock: Clock = { now: () => new Date('2026-11-03T00:00:00Z') };
    const refIdGenerator: RefIdGenerator = () => 'ref-1';
    const validator: InvoiceValidator = {
      validate: (inv) => defaultInvoiceValidator.validate(inv),
      validateBatch: (invs) => defaultInvoiceValidator.validateBatch(invs),
    };
    const templateResolver: TemplateResolver = (templates, ref): TemplateResolution =>
      resolveTemplate(templates, ref);

    await tokenCache.set('k', { value: 't', expiresAt: clock.now() });
    expect((await tokenCache.get('k'))?.expiresAt).toBeInstanceOf(Date);
    expect(refIdGenerator()).toBe('ref-1');
    expect(validator.validate({} as never).length).toBeGreaterThan(0);
    expect(templateResolver([]).kind).toBe('NoActiveTemplate');
  });
});
