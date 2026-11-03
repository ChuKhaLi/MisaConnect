import { describe, expect, it, vi } from 'vitest';
import { fromPino, noopLogger, scrub } from '../../src/infrastructure/logging.js';

describe('scrub', () => {
  it('masks sensitive keys case-insensitively', () => {
    expect(scrub({ Password: 'x', taxCode: 'y', endpoint: 'z' })).toEqual({
      Password: '***',
      taxCode: '***',
      endpoint: 'z',
    });
    const r = scrub({
      Authorization: 'a',
      app_id: 'b',
      'app-id': 'c',
      AppId: 'd',
      accessToken: 'e',
    });
    expect(Object.values(r).every((v) => v === '***')).toBe(true);
  });

  it('scrubs nested objects and arrays without mutating input', () => {
    const input = { a: { password: 'p', ok: 1 }, list: [{ token: 't' }, 'x'] };
    const out = scrub(input);
    expect(out).toEqual({ a: { password: '***', ok: 1 }, list: [{ token: '***' }, 'x'] });
    expect(input.a.password).toBe('p');
    expect(input.list[0]).toEqual({ token: 't' });
  });
});

describe('loggers', () => {
  it('noopLogger does nothing', () => {
    expect(() => noopLogger.info('m', { a: 1 })).not.toThrow();
  });

  it('fromPino maps (msg, fields) to (fields, msg)', () => {
    const p = { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
    const l = fromPino(p);
    l.info('m', { a: 1 });
    l.warn('w');
    expect(p.info).toHaveBeenCalledWith({ a: 1 }, 'm');
    expect(p.warn).toHaveBeenCalledWith({}, 'w');
  });
});
